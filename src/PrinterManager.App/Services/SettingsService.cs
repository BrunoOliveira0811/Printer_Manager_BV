using System.Globalization;
using System.Reflection;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

/// <summary>Parâmetros globais do sistema, persistidos na tabela Settings.</summary>
public sealed class AppSettingsModel
{
    /// <summary>RN04: intervalo mínimo entre varreduras automáticas.</summary>
    public const int MinPollIntervalMinutes = 15;

    /// <summary>RN05: tentativas consecutivas antes de marcar Offline.</summary>
    public const int OfflineAfterAttempts = 3;

    // Alertas (RN01, RN02) — alterações são auditadas (RN14)
    [AlertParameter] public int CriticalSupplyPercent { get; set; } = 5;
    [AlertParameter] public int WearAlertPercent { get; set; } = 90;
    [AlertParameter] public bool SoundAlerts { get; set; } = true;

    // Varredura (RF08)
    public bool PollingEnabled { get; set; } = true;
    public int PollIntervalMinutes { get; set; } = MinPollIntervalMinutes;
    public int SnmpTimeoutMs { get; set; } = 2500;
    public int MaxConcurrentPolls { get; set; } = 16;
    public string DefaultCommunity { get; set; } = "public";

    // Notificações (RF12)
    [AlertParameter] public bool NotifyWarnings { get; set; }
    public bool EmailEnabled { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public bool SmtpUseSsl { get; set; } = true;
    public string SmtpUser { get; set; } = "";
    [Secret] public string SmtpPassword { get; set; } = "";
    public string SmtpFrom { get; set; } = "";
    public string EmailRecipients { get; set; } = "";
    public bool WebhookEnabled { get; set; }
    public string WebhookUrls { get; set; } = "";

    // Backup (RNF08)
    public bool BackupEnabled { get; set; } = true;
    public string BackupDirectory { get; set; } = "";
    public int BackupRetentionDays { get; set; } = 30;

    public string EffectiveBackupDirectory =>
        string.IsNullOrWhiteSpace(BackupDirectory) ? AppPaths.DefaultBackupDirectory : BackupDirectory;

    public AppSettingsModel Clone() => (AppSettingsModel)MemberwiseClone();

    public IEnumerable<string> Validate()
    {
        if (CriticalSupplyPercent is < 1 or > 50) yield return "O limiar crítico de suprimentos deve estar entre 1% e 50%.";
        if (WearAlertPercent is < 50 or > 100) yield return "O limiar de desgaste deve estar entre 50% e 100%.";
        if (PollIntervalMinutes < MinPollIntervalMinutes) yield return $"O intervalo mínimo de varredura é de {MinPollIntervalMinutes} minutos (RN04).";
        if (SnmpTimeoutMs is < 500 or > 30000) yield return "O timeout SNMP deve estar entre 500 e 30000 ms.";
        if (MaxConcurrentPolls is < 1 or > 64) yield return "As consultas simultâneas devem estar entre 1 e 64.";
        if (BackupRetentionDays is < 1 or > 3650) yield return "A retenção de backups deve estar entre 1 e 3650 dias.";
        if (EmailEnabled && (string.IsNullOrWhiteSpace(SmtpHost) || string.IsNullOrWhiteSpace(EmailRecipients)))
            yield return "Para notificar por e-mail informe o servidor SMTP e ao menos um destinatário.";
        if (WebhookEnabled)
        {
            foreach (var url in NotificationService.ParseList(WebhookUrls))
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    yield return $"Webhook inválido (exige HTTPS): {url}";
        }
    }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class AlertParameterAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property)]
public sealed class SecretAttribute : Attribute;

public static class SettingsService
{
    private static readonly PropertyInfo[] Properties = typeof(AppSettingsModel)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite)
        .ToArray();

    public static AppSettingsModel Load()
    {
        using var db = AppDbContext.Create();
        var values = db.Settings.ToDictionary(s => s.Key, s => s.Value);
        var model = new AppSettingsModel();

        foreach (var prop in Properties)
        {
            if (!values.TryGetValue(prop.Name, out var raw) || raw is null) continue;
            if (prop.GetCustomAttribute<SecretAttribute>() is not null) raw = SecretProtector.Unprotect(raw) ?? "";
            try
            {
                prop.SetValue(model, Convert.ChangeType(raw, prop.PropertyType, CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                FileLog.Warn($"Configuracao invalida ignorada: {prop.Name}={raw}", ex);
            }
        }
        return model;
    }

    /// <summary>Salva e audita cada parâmetro alterado (RN03 e RN14).</summary>
    public static void Save(AppSettingsModel updated)
    {
        Session.RequireAdmin();
        var errors = updated.Validate().ToList();
        if (errors.Count > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        var current = Load();
        using var db = AppDbContext.Create();
        var changes = new List<(string Name, string Old, string New, bool IsAlert)>();

        foreach (var prop in Properties)
        {
            var oldValue = Format(prop.GetValue(current));
            var newValue = Format(prop.GetValue(updated));
            if (oldValue == newValue) continue;

            var isSecret = prop.GetCustomAttribute<SecretAttribute>() is not null;
            var stored = isSecret ? SecretProtector.Protect(newValue) : newValue;

            var row = db.Settings.Find(prop.Name);
            if (row is null) db.Settings.Add(new AppSetting { Key = prop.Name, Value = stored });
            else row.Value = stored;

            changes.Add((prop.Name,
                isSecret ? "***" : oldValue,
                isSecret ? "***" : newValue,
                prop.GetCustomAttribute<AlertParameterAttribute>() is not null));
        }

        db.SaveChanges();

        foreach (var change in changes)
        {
            AuditService.Write(
                change.IsAlert ? "Parametro de alerta alterado" : "Configuracao alterada",
                "Configuracao",
                change.Name,
                $"{change.Name}: '{change.Old}' -> '{change.New}'");
        }
    }

    // Valores internos (sem auditoria), ex.: data do último backup.
    public static string? GetInternal(string key)
    {
        using var db = AppDbContext.Create();
        return db.Settings.Find("_" + key)?.Value;
    }

    public static void SetInternal(string key, string? value)
    {
        using var db = AppDbContext.Create();
        var row = db.Settings.Find("_" + key);
        if (row is null) db.Settings.Add(new AppSetting { Key = "_" + key, Value = value });
        else row.Value = value;
        db.SaveChanges();
    }

    private static string Format(object? value) =>
        Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
}
