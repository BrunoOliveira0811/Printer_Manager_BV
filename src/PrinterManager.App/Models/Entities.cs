namespace PrinterManager.App.Models;

// Todas as datas são gravadas em UTC; a conversão para horário local acontece na interface.

public sealed class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string? Email { get; set; }
    public string PasswordHash { get; set; } = "";
    public UserRole Role { get; set; } = UserRole.Visualizador;
    public bool IsActive { get; set; } = true;
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Printer
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public string? Hostname { get; set; }
    public string? Sector { get; set; }
    public string? Location { get; set; }
    public string? Manufacturer { get; set; }
    public string? Model { get; set; }
    public string? SerialNumber { get; set; }
    public string? SysDescription { get; set; }

    // SNMP
    public SnmpVersionOption SnmpVersion { get; set; } = SnmpVersionOption.V2c;
    public int SnmpPort { get; set; } = 161;
    public string Community { get; set; } = "public";
    public string? V3UserName { get; set; }
    public SnmpAuthProtocol V3AuthProtocol { get; set; }
    public string? V3AuthPasswordProtected { get; set; }
    public SnmpPrivProtocol V3PrivProtocol { get; set; }
    public string? V3PrivPasswordProtected { get; set; }
    public string? V3ContextName { get; set; }

    /// <summary>OIDs específicos do fabricante para contadores mono/cor (opcional).</summary>
    public string? MonoCounterOid { get; set; }
    public string? ColorCounterOid { get; set; }

    // Monitoramento
    public LifecycleStatus Lifecycle { get; set; } = LifecycleStatus.Ativa;
    public ConnectionStatus Status { get; set; } = ConnectionStatus.Desconhecido;
    /// <summary>Intervalo próprio de varredura; nulo = usa o global. Mínimo 15 min (RN04).</summary>
    public int? PollIntervalMinutes { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime? LastPollAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public string? LastError { get; set; }
    public bool IsColor { get; set; }
    public long? TotalPages { get; set; }
    public long? MonoPages { get; set; }
    public long? ColorPages { get; set; }
    public string? PanelMessage { get; set; }
    public string? DeviceStatusText { get; set; }

    /// <summary>Franquia mensal contratada (MPS); nulo = sem franquia.</summary>
    public int? MonthlyFranchisePages { get; set; }

    // Soft delete (RN09)
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Supply> Supplies { get; set; } = new();
    public List<WearPart> WearParts { get; set; } = new();

    public string DisplayName => IsDeleted ? $"{Name} (excluída)" : Name;
}

/// <summary>Estado atual de um suprimento/peça reportado pela tabela prtMarkerSupplies.</summary>
public sealed class Supply
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public int SnmpIndex { get; set; }
    public string Description { get; set; } = "";
    public SupplyKind Kind { get; set; }
    public SupplyColor Color { get; set; }
    public int Level { get; set; }
    public int MaxCapacity { get; set; }
    /// <summary>Percentual restante; nulo quando o equipamento não informa nível preciso.</summary>
    public double? Percent { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Histórico de níveis, base do relatório de consumo.</summary>
public sealed class SupplyReading
{
    public long Id { get; set; }
    public int PrinterId { get; set; }
    public string Description { get; set; } = "";
    public SupplyKind Kind { get; set; }
    public SupplyColor Color { get; set; }
    public double Percent { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>Peça de desgaste controlada pelo sistema (RN08).</summary>
public sealed class WearPart
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public string Name { get; set; } = "";
    public long NominalLifePages { get; set; }
    /// <summary>Contador total da impressora na última troca; nulo até a primeira leitura.</summary>
    public long? CounterAtLastReset { get; set; }
    public DateTime? LastResetAt { get; set; }
    public bool IsActive { get; set; } = true;

    public long? PagesSinceReset(long? currentTotal) =>
        currentTotal is null || CounterAtLastReset is null ? null : Math.Max(0, currentTotal.Value - CounterAtLastReset.Value);

    /// <summary>RN08: páginas desde a última troca ÷ vida útil nominal.</summary>
    public double? WearPercent(long? currentTotal) =>
        NominalLifePages <= 0 || PagesSinceReset(currentTotal) is not { } used ? null : used * 100.0 / NominalLifePages;
}

public sealed class CounterReading
{
    public long Id { get; set; }
    public int PrinterId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public long Total { get; set; }
    public long? Mono { get; set; }
    public long? Color { get; set; }
}

public sealed class MaintenanceRecord
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public Printer? Printer { get; set; }
    public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
    public MaintenanceType Type { get; set; }
    public string? PartName { get; set; }
    public string Description { get; set; } = "";
    public long? CounterAtService { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
}

/// <summary>Erros e eventos reportados pelo firmware (prtAlertTable / hrPrinterDetectedErrorState).</summary>
public sealed class PrinterEvent
{
    public long Id { get; set; }
    public int PrinterId { get; set; }
    public string Source { get; set; } = "";
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public AlertSeverity Severity { get; set; }
    public DateTime FirstSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

public sealed class Alert
{
    public long Id { get; set; }
    public int PrinterId { get; set; }
    public Printer? Printer { get; set; }
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    /// <summary>Chave de deduplicação, ex.: "supply:toner preto".</summary>
    public string Key { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
    public DateTime? AcknowledgedAt { get; set; }
    public string? AcknowledgedBy { get; set; }
    public DateTime? NotifiedAt { get; set; }
    public int NotifyAttempts { get; set; }
    public string? LastNotifyError { get; set; }
}

/// <summary>Registro de auditoria encadeado por hash; UPDATE/DELETE bloqueados por trigger (RNF10).</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public int? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string Entity { get; set; } = "";
    public string? EntityId { get; set; }
    public string Details { get; set; } = "";
    public string PreviousHash { get; set; } = "";
    public string Hash { get; set; } = "";
}

public sealed class AppSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

/// <summary>Fechamento mensal de franquia de páginas (RN15).</summary>
public sealed class MonthlyFranchise
{
    public int Id { get; set; }
    public int PrinterId { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public long StartTotal { get; set; }
    public long EndTotal { get; set; }
    public long? StartMono { get; set; }
    public long? EndMono { get; set; }
    public long? StartColor { get; set; }
    public long? EndColor { get; set; }
    public int? FranchisePages { get; set; }
    public DateTime ClosedAt { get; set; } = DateTime.UtcNow;
}
