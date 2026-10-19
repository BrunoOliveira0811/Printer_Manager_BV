using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

/// <summary>Geração e ciclo de vida dos alertas (RF07, RN01, RN02, RN05, RN13).</summary>
public static class AlertService
{
    private sealed record Condition(AlertType Type, AlertSeverity Severity, string Message);

    /// <summary>
    /// Compara as condições atuais da impressora com os alertas abertos: abre os novos e
    /// resolve os que deixaram de existir. Retorna os alertas recém-criados.
    /// </summary>
    public static List<Alert> Evaluate(AppDbContext db, Printer printer, PrinterSnapshot? snapshot, AppSettingsModel settings)
    {
        var open = db.Alerts.Where(a => a.PrinterId == printer.Id && a.ResolvedAt == null).ToList();
        var now = DateTime.UtcNow;

        // RN13: inativas, descartadas e excluídas não geram nem mantêm alertas.
        if (printer.IsDeleted || printer.Lifecycle != LifecycleStatus.Ativa)
        {
            foreach (var a in open) a.ResolvedAt = now;
            return [];
        }

        var conditions = new Dictionary<string, Condition>();
        var offline = snapshot is null;

        if (offline)
        {
            conditions["offline"] = new Condition(AlertType.Offline, AlertSeverity.Critico,
                $"Sem resposta SNMP após {AppSettingsModel.OfflineAfterAttempts} tentativas consecutivas. {printer.LastError}".Trim());
        }
        else
        {
            foreach (var s in snapshot!.Supplies)
            {
                // RN01: toner/tinta com capacidade restante ≤ limiar (padrão 5%)
                if (s.Kind is SupplyKind.Toner or SupplyKind.Tinta && s.Percent is { } p && p <= settings.CriticalSupplyPercent)
                {
                    conditions[$"supply:{s.Description.ToLowerInvariant()}"] = new Condition(AlertType.SuprimentoCritico, AlertSeverity.Critico,
                        $"{s.Description} em nível crítico: {p:0}% (limite {settings.CriticalSupplyPercent}%)");
                }

                // RN02 para peças reportadas pelo próprio equipamento (vida restante)
                if (s.Kind is SupplyKind.Fusor or SupplyKind.Transferencia or SupplyKind.Cilindro && s.Percent is { } remaining
                    && 100 - remaining >= settings.WearAlertPercent)
                {
                    conditions[$"wear-snmp:{s.Description.ToLowerInvariant()}"] = new Condition(AlertType.DesgastePeca, AlertSeverity.Aviso,
                        $"{s.Description} com {100 - remaining:0}% da vida útil consumida — troca recomendada");
                }
            }

            // RN02 + RN08: peças controladas pelo sistema
            foreach (var part in db.WearParts.Where(w => w.PrinterId == printer.Id && w.IsActive).ToList())
            {
                if (part.WearPercent(printer.TotalPages) is { } wear && wear >= settings.WearAlertPercent)
                {
                    conditions[$"wear:{part.Id}"] = new Condition(AlertType.DesgastePeca, AlertSeverity.Aviso,
                        $"{part.Name} atingiu {wear:0}% da vida útil ({part.PagesSinceReset(printer.TotalPages):N0} de {part.NominalLifePages:N0} páginas) — troca recomendada");
                }
            }

            // RF14: erros críticos do firmware
            foreach (var e in snapshot.Events.Where(e => e.Severity == AlertSeverity.Critico))
            {
                conditions[$"error:{e.Source}:{e.Code}"] = new Condition(AlertType.ErroImpressora, AlertSeverity.Critico, e.Description);
            }
        }

        var created = new List<Alert>();
        foreach (var (key, condition) in conditions)
        {
            var existing = open.FirstOrDefault(a => a.Key == key);
            if (existing is not null)
            {
                existing.Message = condition.Message;
                continue;
            }

            var alert = new Alert
            {
                PrinterId = printer.Id,
                Key = key,
                Type = condition.Type,
                Severity = condition.Severity,
                Message = condition.Message,
                CreatedAt = now
            };
            db.Alerts.Add(alert);
            created.Add(alert);
        }

        foreach (var alert in open.Where(a => !conditions.ContainsKey(a.Key)))
        {
            // Sem comunicação não dá para afirmar que suprimentos/peças voltaram ao normal.
            if (offline && alert.Type != AlertType.Offline) continue;
            alert.ResolvedAt = now;
        }

        return created;
    }

    public static void ResolveAllForPrinter(AppDbContext db, int printerId, string reason)
    {
        var now = DateTime.UtcNow;
        foreach (var a in db.Alerts.Where(a => a.PrinterId == printerId && a.ResolvedAt == null))
            a.ResolvedAt = now;
        db.SaveChanges();
    }

    public static void ResolveKey(AppDbContext db, int printerId, string key, string reason)
    {
        var now = DateTime.UtcNow;
        foreach (var a in db.Alerts.Where(a => a.PrinterId == printerId && a.Key == key && a.ResolvedAt == null))
            a.ResolvedAt = now;
        db.SaveChanges();
    }

    /// <summary>Alertas abertos somente de impressoras ativas (RN13).</summary>
    public static IQueryable<Alert> ActiveAlertsQuery(AppDbContext db) =>
        db.Alerts.Where(a => a.ResolvedAt == null && !a.Printer!.IsDeleted && a.Printer.Lifecycle == LifecycleStatus.Ativa);

    public static List<Alert> GetAlerts(bool includeResolved, int days = 30)
    {
        using var db = AppDbContext.Create();
        IQueryable<Alert> q;
        if (includeResolved)
        {
            var since = DateTime.UtcNow.AddDays(-days);
            q = db.Alerts.Where(a => a.CreatedAt >= since);
        }
        else
        {
            q = ActiveAlertsQuery(db);
        }
        return q.AsNoTracking().Include(a => a.Printer)
            .OrderBy(a => a.ResolvedAt != null)
            .ThenByDescending(a => a.Severity)
            .ThenByDescending(a => a.CreatedAt)
            .Take(2000)
            .ToList();
    }

    public static void Acknowledge(long alertId)
    {
        Session.RequireEdit();
        using var db = AppDbContext.Create();
        var alert = db.Alerts.Find(alertId) ?? throw new InvalidOperationException("Alerta não encontrado.");
        if (alert.AcknowledgedAt is not null) return;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedBy = Session.UserLabel;
        db.SaveChanges();
        AuditService.Write("Alerta reconhecido", "Alerta", alertId.ToString(), alert.Message);
    }
}
