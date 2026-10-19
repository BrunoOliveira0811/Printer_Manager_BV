using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

/// <summary>Canal de notificação. Novos canais (Teams, Service Desk, ERP) implementam esta interface (RNF13).</summary>
public interface INotificationChannel
{
    string Name { get; }
    bool IsEnabled(AppSettingsModel settings);
    Task SendAsync(AppSettingsModel settings, IReadOnlyList<Alert> alerts, CancellationToken ct);
}

/// <summary>Notificações multicanal para a equipe técnica (RF12, RN10).</summary>
public static class NotificationService
{
    private const int MaxAttempts = 20;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static IReadOnlyList<INotificationChannel> Channels { get; } = [new EmailChannel(), new WebhookChannel()];

    public static IEnumerable<string> ParseList(string? text) =>
        (text ?? "").Split([';', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Envia os alertas ainda não notificados. Chamado após cada varredura e a cada minuto.</summary>
    public static async Task DispatchPendingAsync(AppSettingsModel settings, CancellationToken ct)
    {
        var channels = Channels.Where(c => c.IsEnabled(settings)).ToList();
        if (channels.Count == 0) return;

        await Gate.WaitAsync(ct);
        try
        {
            using var db = AppDbContext.Create();
            var since = DateTime.UtcNow.AddHours(-24);
            var pending = AlertService.ActiveAlertsQuery(db)
                .Include(a => a.Printer)
                .Where(a => a.NotifiedAt == null && a.NotifyAttempts < MaxAttempts && a.CreatedAt >= since)
                .Where(a => a.Severity == AlertSeverity.Critico || (settings.NotifyWarnings && a.Severity == AlertSeverity.Aviso))
                .OrderBy(a => a.CreatedAt)
                .ToList();
            if (pending.Count == 0) return;

            var errors = new List<string>();
            foreach (var channel in channels)
            {
                try
                {
                    await channel.SendAsync(settings, pending, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"{channel.Name}: {ex.Message}");
                    FileLog.Warn($"Falha ao notificar via {channel.Name}", ex);
                }
            }

            var now = DateTime.UtcNow;
            foreach (var alert in pending)
            {
                alert.NotifyAttempts++;
                // Considera notificado se ao menos um canal entregou; o erro fica registrado.
                if (errors.Count < channels.Count) alert.NotifiedAt = now;
                alert.LastNotifyError = errors.Count == 0 ? null : string.Join(" | ", errors);
            }
            await db.SaveChangesAsync(CancellationToken.None);
        }
        finally
        {
            Gate.Release();
        }
    }

    public static async Task SendTestAsync(AppSettingsModel settings, string channelName)
    {
        var channel = Channels.First(c => c.Name == channelName);
        var fake = new Alert
        {
            Id = 0,
            Type = AlertType.SuprimentoCritico,
            Severity = AlertSeverity.Critico,
            Message = "Mensagem de teste do Gestão de Impressoras",
            CreatedAt = DateTime.UtcNow,
            Printer = new Printer { Name = "Impressora de teste", IpAddress = "0.0.0.0", Sector = "TI" }
        };
        await channel.SendAsync(settings, [fake], CancellationToken.None);
    }

    internal static string FormatLine(Alert a) =>
        $"[{a.Severity.GetDescription()}] {a.Printer?.Name} ({a.Printer?.IpAddress}, setor {a.Printer?.Sector ?? "-"}): {a.Message} — {a.CreatedAt.ToLocalTime():dd/MM/yyyy HH:mm}";

    private sealed class EmailChannel : INotificationChannel
    {
        public string Name => "E-mail";
        public bool IsEnabled(AppSettingsModel s) => s.EmailEnabled && !string.IsNullOrWhiteSpace(s.SmtpHost);

        public async Task SendAsync(AppSettingsModel s, IReadOnlyList<Alert> alerts, CancellationToken ct)
        {
            using var client = new SmtpClient(s.SmtpHost, s.SmtpPort)
            {
                EnableSsl = s.SmtpUseSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                Timeout = 20_000
            };
            if (!string.IsNullOrWhiteSpace(s.SmtpUser))
                client.Credentials = new NetworkCredential(s.SmtpUser, s.SmtpPassword);

            var from = string.IsNullOrWhiteSpace(s.SmtpFrom) ? s.SmtpUser : s.SmtpFrom;
            using var message = new MailMessage { From = new MailAddress(from), SubjectEncoding = Encoding.UTF8, BodyEncoding = Encoding.UTF8 };
            foreach (var to in ParseList(s.EmailRecipients)) message.To.Add(to);

            var critical = alerts.Count(a => a.Severity == AlertSeverity.Critico);
            message.Subject = alerts.Count == 1
                ? $"[Impressoras] {alerts[0].Printer?.Name}: {alerts[0].Type.GetDescription()}"
                : $"[Impressoras] {alerts.Count} alertas ({critical} críticos)";
            message.Body = "Alertas detectados pelo Gestão de Impressoras:\n\n" +
                           string.Join("\n", alerts.Select(FormatLine)) +
                           "\n\nMensagem automática — não responda.";

            await client.SendMailAsync(message, ct);
        }
    }

    private sealed class WebhookChannel : INotificationChannel
    {
        // Somente HTTPS com TLS 1.2/1.3 (RNF06). TLS 1.3 depende do suporte do Windows (Win 11 / Server 2022+).
        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            SslOptions = { EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 },
            PooledConnectionLifetime = TimeSpan.FromMinutes(10)
        })
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public string Name => "Webhook";
        public bool IsEnabled(AppSettingsModel s) => s.WebhookEnabled && ParseList(s.WebhookUrls).Any();

        public async Task SendAsync(AppSettingsModel s, IReadOnlyList<Alert> alerts, CancellationToken ct)
        {
            var payload = new
            {
                source = "gestao-impressoras",
                generatedAt = DateTime.UtcNow,
                // "text" é aceito diretamente por Slack, Microsoft Teams e Google Chat.
                text = string.Join("\n", alerts.Select(FormatLine)),
                alerts = alerts.Select(a => new
                {
                    id = a.Id,
                    type = a.Type.ToString(),
                    severity = a.Severity.ToString(),
                    message = a.Message,
                    createdAt = a.CreatedAt,
                    printer = new { name = a.Printer?.Name, ip = a.Printer?.IpAddress, sector = a.Printer?.Sector, model = a.Printer?.Model, serial = a.Printer?.SerialNumber }
                })
            };
            var json = JsonSerializer.Serialize(payload);

            foreach (var url in ParseList(s.WebhookUrls))
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
                    throw new InvalidOperationException($"Webhook recusado (somente HTTPS): {url}");

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(uri, content, ct);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException($"{uri.Host} respondeu HTTP {(int)response.StatusCode}");
            }
        }
    }
}
