using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

public sealed class PrinterFilter
{
    public string? Text { get; init; }
    public ConnectionStatus? Status { get; init; }
    public LifecycleStatus? Lifecycle { get; init; }
    public string? Sector { get; init; }
    public string? Model { get; init; }
}

/// <summary>Cadastro (RF02), busca/filtro (RF15), peças (RF04/RN08/RN12), manutenções (RF10) e exclusão lógica (RN09).</summary>
public static class PrinterService
{
    public static readonly (string Name, long Life)[] DefaultWearParts =
    {
        ("Unidade de fusão", 100_000),
        ("Rolo de transferência", 120_000),
        ("Rolos de alimentação (pickup)", 50_000),
    };

    public static List<Printer> Search(PrinterFilter filter)
    {
        using var db = AppDbContext.Create();
        var query = db.Printers.AsNoTracking()
            .Include(p => p.Supplies)
            .Include(p => p.WearParts)
            .Where(p => !p.IsDeleted);

        if (filter.Status is { } status) query = query.Where(p => p.Status == status);
        if (filter.Lifecycle is { } lifecycle) query = query.Where(p => p.Lifecycle == lifecycle);
        if (!string.IsNullOrWhiteSpace(filter.Sector)) query = query.Where(p => p.Sector == filter.Sector);
        if (!string.IsNullOrWhiteSpace(filter.Model)) query = query.Where(p => p.Model == filter.Model);
        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var t = filter.Text.Trim().ToLower();
            query = query.Where(p =>
                p.Name.ToLower().Contains(t) ||
                p.IpAddress.Contains(t) ||
                (p.Hostname != null && p.Hostname.ToLower().Contains(t)) ||
                (p.SerialNumber != null && p.SerialNumber.ToLower().Contains(t)) ||
                (p.Location != null && p.Location.ToLower().Contains(t)));
        }

        return query.AsSplitQuery().OrderBy(p => p.Sector).ThenBy(p => p.Name).ToList();
    }

    public static (List<string> Sectors, List<string> Models) GetFilterOptions()
    {
        using var db = AppDbContext.Create();
        var active = db.Printers.Where(p => !p.IsDeleted);
        return (
            active.Where(p => p.Sector != null && p.Sector != "").Select(p => p.Sector!).Distinct().OrderBy(s => s).ToList(),
            active.Where(p => p.Model != null && p.Model != "").Select(p => p.Model!).Distinct().OrderBy(s => s).ToList());
    }

    public static Printer? Get(int id, bool includeDeleted = false)
    {
        using var db = AppDbContext.Create();
        return db.Printers.AsNoTracking()
            .Include(p => p.Supplies)
            .Include(p => p.WearParts)
            .AsSplitQuery()
            .FirstOrDefault(p => p.Id == id && (includeDeleted || !p.IsDeleted));
    }

    public static List<Printer> GetAllIncludingDeleted()
    {
        using var db = AppDbContext.Create();
        return db.Printers.AsNoTracking().OrderBy(p => p.Name).ToList();
    }

    /// <summary>
    /// Inclui ou altera. Senhas SNMPv3 em texto puro: nulo mantém a atual, vazio remove.
    /// </summary>
    public static Printer Save(Printer input, string? v3AuthPassword, string? v3PrivPassword)
    {
        Session.RequireEdit();
        input.Name = input.Name.Trim();
        input.IpAddress = IpRangeService.ParseIpv4(input.IpAddress.Trim()).ToString();
        if (string.IsNullOrWhiteSpace(input.Name)) input.Name = input.Hostname?.Trim() is { Length: > 0 } h ? h : input.IpAddress;
        if (input.PollIntervalMinutes is { } interval && interval < AppSettingsModel.MinPollIntervalMinutes)
            throw new InvalidOperationException($"O intervalo mínimo de varredura é de {AppSettingsModel.MinPollIntervalMinutes} minutos (RN04).");
        if (input.SnmpVersion == SnmpVersionOption.V3 && string.IsNullOrWhiteSpace(input.V3UserName))
            throw new InvalidOperationException("Informe o usuário SNMPv3.");
        if (input.SnmpVersion != SnmpVersionOption.V3 && string.IsNullOrWhiteSpace(input.Community))
            throw new InvalidOperationException("Informe a comunidade SNMP.");

        using var db = AppDbContext.Create();
        if (db.Printers.Any(p => !p.IsDeleted && p.IpAddress == input.IpAddress && p.Id != input.Id))
            throw new InvalidOperationException($"Já existe uma impressora cadastrada com o IP {input.IpAddress}.");

        Printer entity;
        var isNew = input.Id == 0;
        if (isNew)
        {
            entity = new Printer { CreatedAt = DateTime.UtcNow };
            foreach (var (name, life) in DefaultWearParts)
                entity.WearParts.Add(new WearPart { Name = name, NominalLifePages = life });
            db.Printers.Add(entity);
        }
        else
        {
            entity = db.Printers.FirstOrDefault(p => p.Id == input.Id && !p.IsDeleted)
                     ?? throw new InvalidOperationException("Impressora não encontrada.");
        }

        var changes = isNew ? "Cadastro" : Describe(entity, input);
        var previousLifecycle = entity.Lifecycle;

        entity.Name = input.Name;
        entity.IpAddress = input.IpAddress;
        entity.Hostname = Trim(input.Hostname);
        entity.Sector = Trim(input.Sector);
        entity.Location = Trim(input.Location);
        entity.Manufacturer = Trim(input.Manufacturer);
        entity.Model = Trim(input.Model);
        entity.SerialNumber = Trim(input.SerialNumber);
        entity.SnmpVersion = input.SnmpVersion;
        entity.SnmpPort = input.SnmpPort is > 0 and < 65536 ? input.SnmpPort : 161;
        entity.Community = input.Community.Trim();
        entity.V3UserName = Trim(input.V3UserName);
        entity.V3AuthProtocol = input.V3AuthProtocol;
        entity.V3PrivProtocol = input.V3PrivProtocol;
        entity.V3ContextName = Trim(input.V3ContextName);
        if (v3AuthPassword is not null) entity.V3AuthPasswordProtected = SecretProtector.Protect(v3AuthPassword);
        if (v3PrivPassword is not null) entity.V3PrivPasswordProtected = SecretProtector.Protect(v3PrivPassword);
        entity.MonoCounterOid = Trim(input.MonoCounterOid);
        entity.ColorCounterOid = Trim(input.ColorCounterOid);
        entity.Lifecycle = input.Lifecycle;
        entity.PollIntervalMinutes = input.PollIntervalMinutes;
        entity.MonthlyFranchisePages = input.MonthlyFranchisePages;
        entity.UpdatedAt = DateTime.UtcNow;

        db.SaveChanges();

        // RN13: inativas/descartadas não mantêm alertas ativos.
        if (entity.Lifecycle != LifecycleStatus.Ativa && previousLifecycle == LifecycleStatus.Ativa)
            AlertService.ResolveAllForPrinter(db, entity.Id, "Impressora não está ativa");

        AuditService.Write(isNew ? "Impressora cadastrada" : "Impressora alterada", "Impressora", entity.Id.ToString(),
            $"{entity.Name} ({entity.IpAddress}) - {changes}");
        return entity;
    }

    /// <summary>RN09: exige senha de administrador; soft delete preserva o histórico.</summary>
    public static void SoftDelete(int printerId, string adminUser, string adminPassword)
    {
        Session.RequireEdit();
        var check = AuthService.VerifyAdministrator(adminUser, adminPassword);
        if (!check.Success) throw new UnauthorizedAccessException(check.Message);

        using var db = AppDbContext.Create();
        var printer = db.Printers.FirstOrDefault(p => p.Id == printerId && !p.IsDeleted)
                      ?? throw new InvalidOperationException("Impressora não encontrada.");
        printer.IsDeleted = true;
        printer.DeletedAt = DateTime.UtcNow;
        printer.DeletedByUserId = check.User!.Id;
        printer.UpdatedAt = DateTime.UtcNow;
        db.SaveChanges();
        AlertService.ResolveAllForPrinter(db, printerId, "Impressora excluída");

        AuditService.Write("Impressora excluida", "Impressora", printerId.ToString(),
            $"{printer.Name} ({printer.IpAddress}) - exclusao logica confirmada por {check.User.UserName}");
    }

    // ---------- Peças de desgaste ----------

    public static WearPart SaveWearPart(WearPart input)
    {
        Session.RequireEdit();
        if (string.IsNullOrWhiteSpace(input.Name)) throw new InvalidOperationException("Informe o nome da peça.");
        if (input.NominalLifePages <= 0) throw new InvalidOperationException("A vida útil nominal deve ser maior que zero.");

        using var db = AppDbContext.Create();
        WearPart part;
        if (input.Id == 0)
        {
            part = new WearPart { PrinterId = input.PrinterId };
            db.WearParts.Add(part);
        }
        else
        {
            part = db.WearParts.Find(input.Id) ?? throw new InvalidOperationException("Peça não encontrada.");
        }

        var details = $"{part.Name}->{input.Name}; vida {part.NominalLifePages}->{input.NominalLifePages}; contador troca {part.CounterAtLastReset}->{input.CounterAtLastReset}";
        part.Name = input.Name.Trim();
        part.NominalLifePages = input.NominalLifePages;
        part.CounterAtLastReset = input.CounterAtLastReset;
        part.IsActive = input.IsActive;
        db.SaveChanges();

        AuditService.Write(input.Id == 0 ? "Peca cadastrada" : "Peca alterada", "Peca", part.Id.ToString(),
            $"Impressora {part.PrinterId}: {details}");
        return part;
    }

    /// <summary>RN12: reset do contador após troca física; registra a manutenção (RF10).</summary>
    public static void ResetWearPart(int partId, string notes)
    {
        if (!Session.CanResetParts)
            throw new UnauthorizedAccessException("Somente Técnico ou Administrador podem resetar contadores de peças.");

        using var db = AppDbContext.Create();
        var part = db.WearParts.Find(partId) ?? throw new InvalidOperationException("Peça não encontrada.");
        var printer = db.Printers.Find(part.PrinterId)!;

        var previousWear = part.WearPercent(printer.TotalPages);
        part.CounterAtLastReset = printer.TotalPages ?? 0;
        part.LastResetAt = DateTime.UtcNow;

        db.MaintenanceRecords.Add(new MaintenanceRecord
        {
            PrinterId = printer.Id,
            Type = MaintenanceType.Substituicao,
            PartName = part.Name,
            Description = string.IsNullOrWhiteSpace(notes) ? "Substituição da peça e reset do contador" : notes.Trim(),
            CounterAtService = printer.TotalPages,
            UserId = Session.CurrentUser?.Id,
            UserName = Session.UserLabel
        });
        db.SaveChanges();

        AlertService.ResolveKey(db, printer.Id, $"wear:{part.Id}", "Contador da peça resetado");
        AuditService.Write("Contador de peca resetado", "Peca", part.Id.ToString(),
            $"{printer.Name}: {part.Name}; desgaste anterior {previousWear:0.0}%; contador {printer.TotalPages}");
    }

    // ---------- Manutenções ----------

    public static void AddMaintenance(MaintenanceRecord record)
    {
        Session.RequireEdit();
        if (string.IsNullOrWhiteSpace(record.Description)) throw new InvalidOperationException("Descreva o serviço realizado.");
        using var db = AppDbContext.Create();
        record.Id = 0;
        record.Printer = null;
        record.UserId = Session.CurrentUser?.Id;
        record.UserName = Session.UserLabel;
        db.MaintenanceRecords.Add(record);
        db.SaveChanges();
        AuditService.Write("Manutencao registrada", "Manutencao", record.Id.ToString(),
            $"Impressora {record.PrinterId}: {record.Type} {record.PartName} - {record.Description}");
    }

    public static List<MaintenanceRecord> GetMaintenance(int? printerId, DateTime? fromUtc, DateTime? toUtc)
    {
        using var db = AppDbContext.Create();
        var q = db.MaintenanceRecords.AsNoTracking().Include(m => m.Printer).AsQueryable();
        if (printerId is { } id) q = q.Where(m => m.PrinterId == id);
        if (fromUtc is { } from) q = q.Where(m => m.PerformedAt >= from);
        if (toUtc is { } to) q = q.Where(m => m.PerformedAt <= to);
        return q.OrderByDescending(m => m.PerformedAt).Take(2000).ToList();
    }

    // ---------- Históricos ----------

    public static List<PrinterEvent> GetEvents(int printerId) =>
        Query(db => db.PrinterEvents.AsNoTracking().Where(e => e.PrinterId == printerId)
            .OrderByDescending(e => e.IsActive).ThenByDescending(e => e.LastSeenAt).Take(500).ToList());

    public static List<CounterReading> GetCounterHistory(int printerId) =>
        Query(db => db.CounterReadings.AsNoTracking().Where(c => c.PrinterId == printerId)
            .OrderByDescending(c => c.Timestamp).Take(500).ToList());

    private static T Query<T>(Func<AppDbContext, T> query)
    {
        using var db = AppDbContext.Create();
        return query(db);
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Describe(Printer old, Printer updated)
    {
        var changes = new List<string>();
        void Check(string field, object? a, object? b)
        {
            if (!Equals(a?.ToString() ?? "", b?.ToString() ?? "")) changes.Add($"{field}: '{a}' -> '{b}'");
        }
        Check("Nome", old.Name, updated.Name);
        Check("IP", old.IpAddress, updated.IpAddress);
        Check("Hostname", old.Hostname, updated.Hostname);
        Check("Setor", old.Sector, updated.Sector);
        Check("Local", old.Location, updated.Location);
        Check("Modelo", old.Model, updated.Model);
        Check("SNMP", old.SnmpVersion, updated.SnmpVersion);
        if (old.Community != updated.Community) changes.Add("Comunidade SNMP alterada");
        Check("Situacao", old.Lifecycle, updated.Lifecycle);
        Check("Intervalo", old.PollIntervalMinutes, updated.PollIntervalMinutes);
        Check("Franquia", old.MonthlyFranchisePages, updated.MonthlyFranchisePages);
        return changes.Count == 0 ? "sem alteracoes de campos principais" : string.Join("; ", changes);
    }
}
