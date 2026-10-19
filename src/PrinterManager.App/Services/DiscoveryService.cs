using System.Net;
using System.Net.NetworkInformation;
using PrinterManager.App.Data;

namespace PrinterManager.App.Services;

public sealed class ConnectionTestResult
{
    public bool PingOk { get; init; }
    public long? PingMs { get; init; }
    public bool SnmpOk { get; init; }
    public string? SysDescription { get; init; }
    public string? SysName { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public long? TotalPages { get; init; }
    public string? Error { get; init; }

    public string Summary =>
        $"Ping: {(PingOk ? $"OK ({PingMs} ms)" : "sem resposta")}\n" +
        $"SNMP: {(SnmpOk ? "OK" : "falhou")}" +
        (SnmpOk ? $"\nDescrição: {SysDescription}\nModelo: {Model}\nNº de série: {SerialNumber}\nContador total: {TotalPages}" : "") +
        (Error is null ? "" : $"\nErro: {Error}");
}

public sealed class DiscoveredPrinter
{
    public string IpAddress { get; init; } = "";
    public string? Name { get; init; }
    public string? Model { get; init; }
    public string? SerialNumber { get; init; }
    public string? SysDescription { get; init; }
    public bool AlreadyRegistered { get; init; }
}

public sealed record DiscoveryProgress(int Done, int Total, int Found);

/// <summary>RF01 (varredura por faixa) e RF13 (teste ping + SNMP).</summary>
public static class DiscoveryService
{
    public const int MaxAddressesPerScan = 65_536;

    public static async Task<ConnectionTestResult> TestConnectionAsync(string ip, SnmpCredentials credentials, int timeoutMs)
    {
        var address = IpRangeService.ParseIpv4(ip.Trim());

        bool pingOk = false;
        long? pingMs = null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, Math.Min(timeoutMs, 3000));
            pingOk = reply.Status == IPStatus.Success;
            if (pingOk) pingMs = reply.RoundtripTime;
        }
        catch
        {
        }

        try
        {
            var values = await Task.Run(() => new SnmpClient(address, credentials, timeoutMs).Get([
                PrinterSnmpReader.SysDescr, PrinterSnmpReader.SysName, PrinterSnmpReader.HrDeviceDescr,
                PrinterSnmpReader.PrtSerialNumber, PrinterSnmpReader.PrtLifeCount]));

            return new ConnectionTestResult
            {
                PingOk = pingOk,
                PingMs = pingMs,
                SnmpOk = values[PrinterSnmpReader.SysDescr] is not null,
                SysDescription = SnmpClient.AsText(values[PrinterSnmpReader.SysDescr]),
                SysName = SnmpClient.AsText(values[PrinterSnmpReader.SysName]),
                Model = SnmpClient.AsText(values[PrinterSnmpReader.HrDeviceDescr]),
                SerialNumber = SnmpClient.AsText(values[PrinterSnmpReader.PrtSerialNumber]),
                TotalPages = SnmpClient.AsLong(values[PrinterSnmpReader.PrtLifeCount])
            };
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult { PingOk = pingOk, PingMs = pingMs, SnmpOk = false, Error = DescribeError(ex) };
        }
    }

    public static async Task<List<DiscoveredPrinter>> ScanAsync(
        IReadOnlyList<IPAddress> addresses,
        SnmpCredentials credentials,
        int timeoutMs,
        IProgress<DiscoveryProgress>? progress,
        Action<DiscoveredPrinter>? onFound,
        CancellationToken ct)
    {
        if (addresses.Count > MaxAddressesPerScan)
            throw new InvalidOperationException($"A faixa informada tem {addresses.Count:N0} endereços; o máximo por varredura é {MaxAddressesPerScan:N0}.");

        HashSet<string> registered;
        using (var db = AppDbContext.Create())
            registered = db.Printers.Where(p => !p.IsDeleted).Select(p => p.IpAddress).ToHashSet();

        var found = new List<DiscoveredPrinter>();
        var done = 0;

        // Requisições UDP passam a maior parte do tempo aguardando resposta; 64 em paralelo
        // mantém CPU e memória baixas (RNF11) e varre uma /24 em poucos segundos.
        await Parallel.ForEachAsync(addresses, new ParallelOptions { MaxDegreeOfParallelism = 64, CancellationToken = ct },
            async (address, token) =>
            {
                var result = await Task.Run(() => Probe(address, credentials, timeoutMs, registered), token);
                int foundCount;
                lock (found)
                {
                    if (result is not null) found.Add(result);
                    foundCount = found.Count;
                }
                if (result is not null) onFound?.Invoke(result);
                progress?.Report(new DiscoveryProgress(Interlocked.Increment(ref done), addresses.Count, foundCount));
            });

        return found.OrderBy(f => IpRangeService.ToUInt32(IPAddress.Parse(f.IpAddress))).ToList();
    }

    private static DiscoveredPrinter? Probe(IPAddress address, SnmpCredentials credentials, int timeoutMs, HashSet<string> registered)
    {
        try
        {
            var client = new SnmpClient(address, credentials, timeoutMs);
            var values = client.Get([
                PrinterSnmpReader.SysDescr, PrinterSnmpReader.SysName, PrinterSnmpReader.HrDeviceDescr,
                PrinterSnmpReader.PrtPrinterName, PrinterSnmpReader.PrtSerialNumber, PrinterSnmpReader.PrtLifeCount]);

            var descr = SnmpClient.AsText(values[PrinterSnmpReader.SysDescr]);
            if (descr is null) return null;

            // Só considera impressora quem implementa a Printer-MIB ou se descreve como tal.
            var isPrinter = values[PrinterSnmpReader.PrtLifeCount] is not null
                            || values[PrinterSnmpReader.PrtPrinterName] is not null
                            || LooksLikePrinter(descr);
            if (!isPrinter) return null;

            var ip = address.ToString();
            return new DiscoveredPrinter
            {
                IpAddress = ip,
                Name = SnmpClient.AsText(values[PrinterSnmpReader.PrtPrinterName]) ?? SnmpClient.AsText(values[PrinterSnmpReader.SysName]),
                Model = SnmpClient.AsText(values[PrinterSnmpReader.HrDeviceDescr]),
                SerialNumber = SnmpClient.AsText(values[PrinterSnmpReader.PrtSerialNumber]),
                SysDescription = descr,
                AlreadyRegistered = registered.Contains(ip)
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikePrinter(string descr)
    {
        var d = descr.ToLowerInvariant();
        string[] keywords = ["printer", "laserjet", "officejet", "deskjet", "mfp", "impressora", "kyocera", "ecosys",
            "lexmark", "brother", "epson", "ricoh", "aficio", "xerox", "canon", "imagerunner", "samsung", "konica",
            "bizhub", "sharp", "oki", "pantum", "elgin", "zebra"];
        return keywords.Any(d.Contains);
    }

    public static string DescribeError(Exception ex) => ex switch
    {
        Lextm.SharpSnmpLib.Messaging.TimeoutException => "Tempo esgotado aguardando resposta SNMP (verifique IP, comunidade/credenciais e firewall UDP 161).",
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionReset or System.Net.Sockets.SocketError.ConnectionRefused }
            => "O equipamento recusou a conexão na porta SNMP (serviço SNMP desabilitado ou porta incorreta).",
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable }
            => "Endereço inacessível a partir desta máquina (rota ou VLAN).",
        SnmpProtocolException or SnmpErrorStatusException => ex.Message,
        _ => ex.Message
    };
}
