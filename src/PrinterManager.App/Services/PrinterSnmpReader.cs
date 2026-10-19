using System.Net;
using Lextm.SharpSnmpLib;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

public sealed record SupplySnapshot(
    int Index, string Description, SupplyKind Kind, SupplyColor Color, int Level, int MaxCapacity, double? Percent);

public sealed record FirmwareEvent(string Source, string Code, string Description, AlertSeverity Severity);

public sealed class PrinterSnapshot
{
    public string? SysDescription { get; set; }
    public string? SysName { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? PanelMessage { get; set; }
    public int? DeviceStatus { get; set; }
    public int? PrinterStatus { get; set; }
    public long? TotalPages { get; set; }
    public long? MonoPages { get; set; }
    public long? ColorPages { get; set; }
    public List<SupplySnapshot> Supplies { get; } = new();
    public List<FirmwareEvent> Events { get; } = new();

    public bool IsColor => Supplies.Any(s =>
        s.Kind is SupplyKind.Toner or SupplyKind.Tinta && s.Color is SupplyColor.Ciano or SupplyColor.Magenta or SupplyColor.Amarelo);

    public string DeviceStatusText => (DeviceStatus, PrinterStatus) switch
    {
        (5, _) => "Inoperante",
        (_, 4) => "Imprimindo",
        (_, 5) => "Aquecendo",
        (3, _) => "Atenção",
        (4, _) => "Em teste",
        (_, 3) => "Pronta",
        (2, _) => "Em funcionamento",
        _      => "Desconhecido"
    };
}

/// <summary>
/// Lê suprimentos, contadores e erros pela Printer-MIB (RFC 3805) e Host-Resources-MIB (RFC 2790).
/// Atende RF03, RF04, RF05 e RF14.
/// </summary>
public static class PrinterSnmpReader
{
    public const string SysDescr          = "1.3.6.1.2.1.1.1.0";
    public const string SysName           = "1.3.6.1.2.1.1.5.0";
    public const string HrDeviceDescr     = "1.3.6.1.2.1.25.3.2.1.3.1";
    public const string HrDeviceStatus    = "1.3.6.1.2.1.25.3.2.1.5.1";
    public const string HrPrinterStatus   = "1.3.6.1.2.1.25.3.5.1.1.1";
    public const string HrPrinterErrors   = "1.3.6.1.2.1.25.3.5.1.2.1";
    public const string PrtSerialNumber   = "1.3.6.1.2.1.43.5.1.1.17.1";
    public const string PrtPrinterName    = "1.3.6.1.2.1.43.5.1.1.16.1";
    public const string PrtLifeCount      = "1.3.6.1.2.1.43.10.2.1.4.1.1";
    public const string PrtConsoleDisplay = "1.3.6.1.2.1.43.16.5.1.2.1.1";
    private const string SuppliesTable    = "1.3.6.1.2.1.43.11.1.1";
    private const string ColorantValues   = "1.3.6.1.2.1.43.12.1.1.4";
    private const string AlertTable       = "1.3.6.1.2.1.43.18.1.1";

    /// <summary>
    /// Perfis de contadores mono/cor por fabricante (MIBs proprietárias). Valide no seu parque;
    /// OIDs específicos podem ser informados no cadastro da impressora.
    /// </summary>
    private static readonly (string Match, string MonoOid, string ColorOid)[] VendorCounterProfiles =
    {
        ("hp", "1.3.6.1.4.1.11.2.3.9.4.2.1.4.1.2.6.0", "1.3.6.1.4.1.11.2.3.9.4.2.1.4.1.2.7.0"),
    };

    public static PrinterSnapshot Read(IPAddress address, SnmpCredentials credentials, int timeoutMs,
        string? monoCounterOid = null, string? colorCounterOid = null)
    {
        var client = new SnmpClient(address, credentials, timeoutMs);
        var snapshot = new PrinterSnapshot();

        // 1. Dados básicos — se esta consulta falhar, a impressora é considerada sem resposta.
        var basic = client.Get([SysDescr, SysName, HrDeviceDescr, HrDeviceStatus, HrPrinterStatus,
            HrPrinterErrors, PrtSerialNumber, PrtLifeCount, PrtConsoleDisplay]);

        snapshot.SysDescription = SnmpClient.AsText(basic[SysDescr]);
        snapshot.SysName        = SnmpClient.AsText(basic[SysName]);
        snapshot.Model          = SnmpClient.AsText(basic[HrDeviceDescr]);
        snapshot.SerialNumber   = SnmpClient.AsText(basic[PrtSerialNumber]);
        snapshot.PanelMessage   = SnmpClient.AsText(basic[PrtConsoleDisplay]);
        snapshot.DeviceStatus   = (int?)SnmpClient.AsLong(basic[HrDeviceStatus]);
        snapshot.PrinterStatus  = (int?)SnmpClient.AsLong(basic[HrPrinterStatus]);
        snapshot.TotalPages     = SnmpClient.AsLong(basic[PrtLifeCount]);

        if (snapshot.DeviceStatus == 5)
            snapshot.Events.Add(new FirmwareEvent("hrDeviceStatus", "down", "Dispositivo reporta estado inoperante", AlertSeverity.Critico));

        if (basic[HrPrinterErrors] is OctetString errorBits)
            snapshot.Events.AddRange(DecodeErrorState(errorBits.GetRaw()));

        // 2. Suprimentos e peças
        ReadSupplies(client, snapshot);

        // 3. Tabela de alertas do firmware
        ReadAlertTable(client, snapshot);

        // 4. Contadores mono/cor
        ReadColorCounters(client, snapshot, monoCounterOid, colorCounterOid);

        return snapshot;
    }

    private static void ReadSupplies(SnmpClient client, PrinterSnapshot snapshot)
    {
        IList<Variable> rows, colorants;
        try { rows = client.Walk(SuppliesTable); }
        catch (Exception ex) { FileLog.Warn($"{client.Endpoint}: falha ao ler prtMarkerSuppliesTable", ex); return; }
        try { colorants = client.Walk(ColorantValues); }
        catch { colorants = []; }

        var colorantNames = colorants.ToDictionary(
            v => v.Id.ToString()[(ColorantValues.Length + 1)..],
            v => SnmpClient.AsText(v.Data) ?? "");

        // OID = SuppliesTable.<coluna>.<hrDeviceIndex>.<índice>
        var table = new Dictionary<string, Dictionary<int, ISnmpData>>();
        foreach (var v in rows)
        {
            var suffix = v.Id.ToString()[(SuppliesTable.Length + 1)..];
            var dot = suffix.IndexOf('.');
            if (dot < 0 || !int.TryParse(suffix[..dot], out var column)) continue;
            var key = suffix[(dot + 1)..];
            if (!table.TryGetValue(key, out var cols)) table[key] = cols = new();
            cols[column] = v.Data;
        }

        foreach (var (key, cols) in table.OrderBy(t => t.Key, StringComparer.Ordinal))
        {
            var index = int.TryParse(key.Split('.').Last(), out var i) ? i : 0;
            var description = SnmpClient.AsText(cols.GetValueOrDefault(6)) ?? $"Suprimento {index}";
            var supplyClass = (int)(SnmpClient.AsLong(cols.GetValueOrDefault(4)) ?? 0);
            var type        = (int)(SnmpClient.AsLong(cols.GetValueOrDefault(5)) ?? 0);
            var max         = (int)(SnmpClient.AsLong(cols.GetValueOrDefault(8)) ?? -2);
            var level       = (int)(SnmpClient.AsLong(cols.GetValueOrDefault(9)) ?? -2);
            var colorantIdx = SnmpClient.AsLong(cols.GetValueOrDefault(3)) ?? 0;

            var deviceIndex = key.Split('.').First();
            var colorant = colorantNames.GetValueOrDefault($"{deviceIndex}.{colorantIdx}");

            var kind = MapKind(type, supplyClass);
            var color = kind is SupplyKind.Toner or SupplyKind.Tinta or SupplyKind.Cilindro
                ? MapColor(colorant, description)
                : SupplyColor.Outro;

            // -3 = "há suprimento, sem medição precisa"; -2 = desconhecido; -1 = sem limite
            double? percent = max > 0 && level >= 0 ? Math.Min(100.0, level * 100.0 / max) : null;

            snapshot.Supplies.Add(new SupplySnapshot(index, description, kind, color, level, max, percent));
        }
    }

    private static void ReadAlertTable(SnmpClient client, PrinterSnapshot snapshot)
    {
        IList<Variable> rows;
        try { rows = client.Walk(AlertTable); }
        catch { return; }

        var table = new Dictionary<string, Dictionary<int, ISnmpData>>();
        foreach (var v in rows)
        {
            var suffix = v.Id.ToString()[(AlertTable.Length + 1)..];
            var dot = suffix.IndexOf('.');
            if (dot < 0 || !int.TryParse(suffix[..dot], out var column)) continue;
            var key = suffix[(dot + 1)..];
            if (!table.TryGetValue(key, out var cols)) table[key] = cols = new();
            cols[column] = v.Data;
        }

        foreach (var cols in table.Values)
        {
            var severity = (int)(SnmpClient.AsLong(cols.GetValueOrDefault(2)) ?? 1);
            var code     = (int)(SnmpClient.AsLong(cols.GetValueOrDefault(7)) ?? 1);
            var text     = SnmpClient.AsText(cols.GetValueOrDefault(8));
            var codeName = AlertCodeName(code);

            snapshot.Events.Add(new FirmwareEvent(
                "prtAlertTable",
                $"{code}",
                string.IsNullOrWhiteSpace(text) ? codeName : $"{text} ({codeName})",
                severity switch { 3 => AlertSeverity.Critico, 4 or 5 => AlertSeverity.Aviso, _ => AlertSeverity.Info }));
        }
    }

    private static void ReadColorCounters(SnmpClient client, PrinterSnapshot snapshot, string? monoOid, string? colorOid)
    {
        if (string.IsNullOrWhiteSpace(monoOid) && string.IsNullOrWhiteSpace(colorOid))
        {
            var descr = (snapshot.SysDescription ?? "").ToLowerInvariant();
            var profile = VendorCounterProfiles.FirstOrDefault(p => descr.Contains(p.Match));
            if (profile.Match is not null && snapshot.IsColor)
            {
                monoOid = profile.MonoOid;
                colorOid = profile.ColorOid;
            }
        }

        if (!string.IsNullOrWhiteSpace(monoOid) || !string.IsNullOrWhiteSpace(colorOid))
        {
            try
            {
                var oids = new[] { monoOid, colorOid }.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o!.Trim()).ToList();
                var values = client.Get(oids);
                if (!string.IsNullOrWhiteSpace(monoOid)) snapshot.MonoPages = SnmpClient.AsLong(values[monoOid.Trim()]);
                if (!string.IsNullOrWhiteSpace(colorOid)) snapshot.ColorPages = SnmpClient.AsLong(values[colorOid.Trim()]);
            }
            catch (Exception ex)
            {
                FileLog.Warn($"{client.Endpoint}: falha ao ler contadores mono/cor", ex);
            }
        }

        // Impressora monocromática: todo o volume é mono.
        if (!snapshot.IsColor && snapshot.TotalPages is { } total)
        {
            snapshot.MonoPages ??= total;
            snapshot.ColorPages ??= 0;
        }
        // Se só um dos contadores veio, deriva o outro pelo total.
        else if (snapshot.TotalPages is { } t)
        {
            if (snapshot.MonoPages is { } m && snapshot.ColorPages is null && m <= t) snapshot.ColorPages = t - m;
            if (snapshot.ColorPages is { } c && snapshot.MonoPages is null && c <= t) snapshot.MonoPages = t - c;
        }
    }

    // prtMarkerSuppliesTypeTC (RFC 3805)
    private static SupplyKind MapKind(int type, int supplyClass)
    {
        if (supplyClass == 3) return SupplyKind.Residuo; // receptacleThatIsFilled
        return type switch
        {
            3 or 21              => SupplyKind.Toner,
            5 or 6 or 7          => SupplyKind.Tinta,
            4 or 8 or 14 or 24 or 26 => SupplyKind.Residuo,
            9 or 10              => SupplyKind.Cilindro,
            11 or 15 or 17 or 19 or 22 => SupplyKind.Fusor,
            20                   => SupplyKind.Transferencia,
            _                    => SupplyKind.Outro
        };
    }

    public static SupplyColor MapColor(string? colorant, string description)
    {
        var text = $"{colorant} {description}".ToLowerInvariant();
        if (text.Contains("cyan") || text.Contains("ciano")) return SupplyColor.Ciano;
        if (text.Contains("magenta")) return SupplyColor.Magenta;
        if (text.Contains("yellow") || text.Contains("amarelo")) return SupplyColor.Amarelo;
        if (text.Contains("black") || text.Contains("preto") || text.Contains("negro") || text.Contains("mono"))
            return SupplyColor.Preto;
        // Muitas impressoras monocromáticas descrevem apenas "Toner Cartridge".
        return string.IsNullOrWhiteSpace(colorant) ? SupplyColor.Preto : SupplyColor.Outro;
    }

    private static IEnumerable<FirmwareEvent> DecodeErrorState(byte[] bytes)
    {
        (int Byte, int Mask, string Code, string Text, AlertSeverity Severity)[] bits =
        {
            (0, 0x80, "lowPaper",            "Papel baixo",                      AlertSeverity.Aviso),
            (0, 0x40, "noPaper",             "Sem papel",                        AlertSeverity.Critico),
            (0, 0x20, "lowToner",            "Toner baixo",                      AlertSeverity.Aviso),
            (0, 0x10, "noToner",             "Sem toner",                        AlertSeverity.Critico),
            (0, 0x08, "doorOpen",            "Porta aberta",                     AlertSeverity.Critico),
            (0, 0x04, "jammed",              "Atolamento de papel",              AlertSeverity.Critico),
            (0, 0x02, "offline",             "Impressora em modo offline",       AlertSeverity.Critico),
            (0, 0x01, "serviceRequested",    "Serviço técnico solicitado",       AlertSeverity.Critico),
            (1, 0x80, "inputTrayMissing",    "Bandeja de entrada ausente",       AlertSeverity.Critico),
            (1, 0x40, "outputTrayMissing",   "Bandeja de saída ausente",         AlertSeverity.Critico),
            (1, 0x20, "markerSupplyMissing", "Suprimento ausente",               AlertSeverity.Critico),
            (1, 0x10, "outputNearFull",      "Bandeja de saída quase cheia",     AlertSeverity.Aviso),
            (1, 0x08, "outputFull",          "Bandeja de saída cheia",           AlertSeverity.Critico),
            (1, 0x04, "inputTrayEmpty",      "Bandeja de entrada vazia",         AlertSeverity.Aviso),
            (1, 0x02, "overduePreventMaint", "Manutenção preventiva vencida",    AlertSeverity.Aviso),
        };

        foreach (var bit in bits)
        {
            if (bytes.Length > bit.Byte && (bytes[bit.Byte] & bit.Mask) != 0)
                yield return new FirmwareEvent("hrPrinterDetectedErrorState", bit.Code, bit.Text, bit.Severity);
        }
    }

    // prtAlertCodeTC (RFC 3805) — principais códigos
    private static string AlertCodeName(int code) => code switch
    {
        3 => "Tampa aberta",
        8 => "Atolamento",
        9 => "Subunidade ausente",
        10 => "Vida útil da subunidade quase no fim",
        11 => "Vida útil da subunidade esgotada",
        12 => "Subunidade quase vazia",
        13 => "Subunidade vazia",
        14 => "Subunidade quase cheia",
        15 => "Subunidade cheia",
        22 => "Subunidade offline",
        29 => "Falha recuperável",
        30 => "Falha irrecuperável",
        33 => "Falha de motor",
        36 => "Superaquecimento",
        501 => "Porta aberta",
        801 => "Bandeja de entrada ausente",
        807 => "Papel baixo",
        808 => "Sem papel",
        901 => "Bandeja de saída ausente",
        902 => "Bandeja de saída quase cheia",
        903 => "Bandeja de saída cheia",
        1001 => "Fusor abaixo da temperatura",
        1002 => "Fusor acima da temperatura",
        1101 => "Toner vazio",
        1102 => "Tinta vazia",
        1104 => "Toner quase vazio",
        1105 => "Tinta quase vazia",
        1107 => "Recipiente de resíduo de toner quase cheio",
        1108 => "Recipiente de resíduo de tinta quase cheio",
        1109 => "Recipiente de resíduo de toner cheio",
        1110 => "Recipiente de resíduo de tinta cheio",
        1111 => "Cilindro quase no fim da vida útil",
        1112 => "Cilindro no fim da vida útil",
        1301 => "Bandeja do caminho de mídia ausente",
        1302 => "Caminho de mídia quase cheio",
        1303 => "Caminho de mídia cheio",
        _ => $"Código {code}"
    };
}
