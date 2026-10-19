using System.Collections.Concurrent;
using System.Net;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Models;

namespace PrinterManager.App.Services;

public sealed class PollCompletedEventArgs(IReadOnlyList<int> printerIds, IReadOnlyList<Alert> newAlerts) : EventArgs
{
    public IReadOnlyList<int> PrinterIds { get; } = printerIds;
    public IReadOnlyList<Alert> NewAlerts { get; } = newAlerts;
}

/// <summary>
/// Agendador de varreduras (RF08). A cada minuto verifica quais impressoras estão vencidas
/// (intervalo próprio ou global, nunca menor que 15 min — RN04) e as consulta em paralelo
/// com limite de concorrência (RNF03/RNF11). Também executa as rotinas diárias/mensais.
/// </summary>
public sealed class PollingService : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<int, byte> inFlight = new();
    private readonly CancellationTokenSource cts = new();
    private Task? loop;
    private int cycleRunning;

    public event EventHandler<PollCompletedEventArgs>? PollCompleted;
    public event EventHandler? StateChanged;

    public DateTime? LastCycleAt { get; private set; }
    public bool IsBusy => !inFlight.IsEmpty;
    public int InFlightCount => inFlight.Count;

    public void Start()
    {
        loop ??= Task.Run(() => RunAsync(cts.Token));
    }

    private async Task RunAsync(CancellationToken ct)
    {
        // Primeiro ciclo logo na abertura, depois a cada minuto.
        using var timer = new PeriodicTimer(TickInterval);
        do
        {
            try
            {
                await TickAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                FileLog.Error("Falha no ciclo do agendador", ex);
            }
        }
        while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task TickAsync(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref cycleRunning, 1) == 1) return;
        try
        {
            var settings = SettingsService.Load();

            if (settings.PollingEnabled)
            {
                var due = GetDuePrinterIds(settings);
                if (due.Count > 0) await PollAsync(due, settings, ct);
            }

            // Reenvio de notificações pendentes: garante o prazo de 5 minutos (RN10).
            await NotificationService.DispatchPendingAsync(settings, ct);
            FranchiseService.CloseDueMonths();
            BackupService.RunIfDue(settings);
            LastCycleAt = DateTime.UtcNow;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            Interlocked.Exchange(ref cycleRunning, 0);
        }
    }

    private static List<int> GetDuePrinterIds(AppSettingsModel settings)
    {
        using var db = AppDbContext.Create();
        var now = DateTime.UtcNow;
        return db.Printers.AsNoTracking()
            .Where(p => !p.IsDeleted && p.Lifecycle == LifecycleStatus.Ativa)
            .Select(p => new { p.Id, p.LastPollAt, p.PollIntervalMinutes })
            .AsEnumerable()
            .Where(p =>
            {
                var interval = Math.Max(AppSettingsModel.MinPollIntervalMinutes, p.PollIntervalMinutes ?? settings.PollIntervalMinutes);
                return p.LastPollAt is null || now - p.LastPollAt.Value >= TimeSpan.FromMinutes(interval) - TimeSpan.FromSeconds(30);
            })
            .Select(p => p.Id)
            .ToList();
    }

    /// <summary>Atualização sob demanda (botão "Atualizar agora").</summary>
    public Task PollNowAsync(IEnumerable<int> printerIds, CancellationToken ct = default)
    {
        var settings = SettingsService.Load();
        return PollAsync(printerIds.ToList(), settings, ct);
    }

    private async Task PollAsync(IReadOnlyList<int> ids, AppSettingsModel settings, CancellationToken ct)
    {
        var newAlerts = new ConcurrentBag<Alert>();
        var polled = new ConcurrentBag<int>();

        await Parallel.ForEachAsync(ids,
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrentPolls, CancellationToken = ct },
            async (id, token) =>
            {
                if (!inFlight.TryAdd(id, 0)) return;
                StateChanged?.Invoke(this, EventArgs.Empty);
                try
                {
                    foreach (var alert in await PollPrinterAsync(id, settings, token)) newAlerts.Add(alert);
                    polled.Add(id);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    FileLog.Error($"Falha ao processar varredura da impressora {id}", ex);
                }
                finally
                {
                    inFlight.TryRemove(id, out _);
                }
            });

        var alerts = newAlerts.ToList();
        if (alerts.Count > 0)
        {
            // RN10: disparo imediato após a confirmação na varredura.
            try { await NotificationService.DispatchPendingAsync(settings, ct); }
            catch (Exception ex) { FileLog.Error("Falha ao disparar notificacoes", ex); }
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        PollCompleted?.Invoke(this, new PollCompletedEventArgs(polled.ToList(), alerts));
    }

    private static async Task<List<Alert>> PollPrinterAsync(int printerId, AppSettingsModel settings, CancellationToken ct)
    {
        Printer printer;
        using (var db = AppDbContext.Create())
        {
            printer = db.Printers.AsNoTracking().FirstOrDefault(p => p.Id == printerId && !p.IsDeleted)
                      ?? throw new InvalidOperationException("Impressora não encontrada");
        }

        var credentials = SnmpCredentials.FromPrinter(printer);
        var address = IPAddress.Parse(printer.IpAddress);
        PrinterSnapshot? snapshot = null;
        string? lastError = null;
        var attempts = 0;

        // RN05: até 3 tentativas consecutivas antes de declarar Offline.
        while (attempts < AppSettingsModel.OfflineAfterAttempts && snapshot is null)
        {
            ct.ThrowIfCancellationRequested();
            attempts++;
            try
            {
                snapshot = await Task.Run(() => PrinterSnmpReader.Read(address, credentials, settings.SnmpTimeoutMs,
                    printer.MonoCounterOid, printer.ColorCounterOid), ct);
                if (snapshot.SysDescription is null && snapshot.TotalPages is null && snapshot.Supplies.Count == 0)
                {
                    snapshot = null;
                    lastError = "O equipamento respondeu sem dados de impressora.";
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = DiscoveryService.DescribeError(ex);
            }

            if (snapshot is null && attempts < AppSettingsModel.OfflineAfterAttempts)
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
        }

        using var ctx = AppDbContext.Create();
        var entity = ctx.Printers.Include(p => p.Supplies).First(p => p.Id == printerId);
        var now = DateTime.UtcNow;
        entity.LastPollAt = now;

        if (snapshot is null)
        {
            entity.ConsecutiveFailures += attempts;
            entity.LastError = lastError;
            if (entity.ConsecutiveFailures >= AppSettingsModel.OfflineAfterAttempts)
                entity.Status = ConnectionStatus.Offline;
        }
        else
        {
            ApplySnapshot(ctx, entity, snapshot, now);
        }

        var created = AlertService.Evaluate(ctx, entity, snapshot, settings);
        await ctx.SaveChangesAsync(CancellationToken.None);

        if (snapshot is not null)
        {
            var hasCriticalError = snapshot.Events.Any(e => e.Severity == AlertSeverity.Critico);
            var hasOpenAlerts = ctx.Alerts.Any(a => a.PrinterId == entity.Id && a.ResolvedAt == null);
            entity.Status = hasCriticalError ? ConnectionStatus.Erro
                : hasOpenAlerts ? ConnectionStatus.Alerta
                : ConnectionStatus.Online;
        }

        await ctx.SaveChangesAsync(CancellationToken.None);
        return created;
    }

    private static void ApplySnapshot(AppDbContext db, Printer entity, PrinterSnapshot snap, DateTime now)
    {
        entity.ConsecutiveFailures = 0;
        entity.LastSuccessAt = now;
        entity.LastError = null;
        entity.SysDescription = snap.SysDescription ?? entity.SysDescription;
        entity.Model ??= snap.Model;
        entity.SerialNumber ??= snap.SerialNumber;
        entity.Hostname ??= snap.SysName;
        entity.PanelMessage = snap.PanelMessage;
        entity.DeviceStatusText = snap.DeviceStatusText;
        entity.IsColor = snap.IsColor;
        if (snap.TotalPages is not null) entity.TotalPages = snap.TotalPages;
        if (snap.MonoPages is not null) entity.MonoPages = snap.MonoPages;
        if (snap.ColorPages is not null) entity.ColorPages = snap.ColorPages;

        // Contadores (RF05): grava quando muda ou, no mínimo, uma vez por dia (base do RN06/RN15).
        if (snap.TotalPages is { } total)
        {
            var last = db.CounterReadings.Where(c => c.PrinterId == entity.Id).OrderByDescending(c => c.Timestamp).FirstOrDefault();
            if (last is null || last.Total != total || last.Mono != snap.MonoPages || last.Color != snap.ColorPages
                || now - last.Timestamp >= TimeSpan.FromHours(24))
            {
                db.CounterReadings.Add(new CounterReading
                {
                    PrinterId = entity.Id, Timestamp = now, Total = total, Mono = snap.MonoPages, Color = snap.ColorPages
                });
            }

            // Peças recém-cadastradas começam a contar a partir da primeira leitura.
            foreach (var part in db.WearParts.Where(w => w.PrinterId == entity.Id && w.CounterAtLastReset == null))
                part.CounterAtLastReset = total;
        }

        // Suprimentos (RF03/RF04): estado atual + histórico de variações.
        var lastReadings = entity.Supplies
            .Where(s => s.Percent is not null)
            .DistinctBy(s => s.Description)
            .ToDictionary(s => s.Description, s => s.Percent!.Value);

        db.Supplies.RemoveRange(entity.Supplies);
        foreach (var s in snap.Supplies)
        {
            db.Supplies.Add(new Supply
            {
                PrinterId = entity.Id, SnmpIndex = s.Index, Description = s.Description, Kind = s.Kind,
                Color = s.Color, Level = s.Level, MaxCapacity = s.MaxCapacity, Percent = s.Percent, UpdatedAt = now
            });

            if (s.Percent is { } p && (!lastReadings.TryGetValue(s.Description, out var previous) || Math.Abs(previous - p) >= 0.5))
            {
                db.SupplyReadings.Add(new SupplyReading
                {
                    PrinterId = entity.Id, Description = s.Description, Kind = s.Kind, Color = s.Color, Percent = p, Timestamp = now
                });
            }
        }

        // Eventos do firmware (RF14): mantém histórico, reativa/encerra conforme a leitura.
        var openEvents = db.PrinterEvents.Where(e => e.PrinterId == entity.Id && e.IsActive).ToList();
        var seen = new HashSet<long>();
        foreach (var ev in snap.Events.DistinctBy(e => (e.Source, e.Code, e.Description)))
        {
            var match = openEvents.FirstOrDefault(o => o.Source == ev.Source && o.Code == ev.Code && o.Description == ev.Description);
            if (match is not null)
            {
                match.LastSeenAt = now;
                match.Severity = ev.Severity;
                seen.Add(match.Id);
            }
            else
            {
                db.PrinterEvents.Add(new PrinterEvent
                {
                    PrinterId = entity.Id, Source = ev.Source, Code = ev.Code, Description = ev.Description,
                    Severity = ev.Severity, FirstSeenAt = now, LastSeenAt = now
                });
            }
        }
        foreach (var e in openEvents.Where(e => !seen.Contains(e.Id))) e.IsActive = false;
    }

    public void Dispose()
    {
        if (cts.IsCancellationRequested) return;
        cts.Cancel();
    }
}
