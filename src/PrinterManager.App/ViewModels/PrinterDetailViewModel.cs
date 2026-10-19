using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

public sealed class WearPartRowViewModel
{
    public WearPart Part { get; init; } = new();
    public string Name => Part.Name;
    public long NominalLife => Part.NominalLifePages;
    public long? UsedPages { get; init; }
    public double? WearPercent { get; init; }
    public double BarPercent => Math.Min(100, WearPercent ?? 0);
    public bool NeedsReplacement { get; init; }
    public string WearText => WearPercent is { } w ? $"{w:0.0}%" : "Aguardando leitura";
    public string Status => NeedsReplacement ? "Troca recomendada" : Part.IsActive ? "OK" : "Desativada";
    public DateTime? LastResetAt => Part.LastResetAt;
}

/// <summary>Detalhes da impressora: suprimentos, peças, contadores, erros e manutenções (RF03–RF05, RF10, RF14).</summary>
public sealed class PrinterDetailViewModel : ObservableObject
{
    private readonly int printerId;
    private readonly PollingService polling;
    private Printer? printer;
    private string busyText = "";

    public PrinterDetailViewModel(int printerId, PollingService polling)
    {
        this.printerId = printerId;
        this.polling = polling;

        PollNowCommand = new AsyncRelayCommand(async () =>
        {
            BusyText = "Consultando impressora…";
            try { await Task.Run(() => polling.PollNowAsync([printerId])); }
            finally { BusyText = ""; }
            await LoadAsync();
        });
        EditCommand = new AsyncRelayCommand(async () =>
        {
            if (PrinterEditWindow.ShowEdit(printerId)) await LoadAsync();
        }, () => Session.CanEdit);
        AddPartCommand = new AsyncRelayCommand(async () =>
        {
            if (WearPartEditWindow.Show(new WearPart { PrinterId = printerId, NominalLifePages = 100_000, CounterAtLastReset = printer?.TotalPages }))
                await LoadAsync();
        }, () => Session.CanEdit);
        EditPartCommand = new AsyncRelayCommand(async p =>
        {
            if (p is WearPartRowViewModel row && WearPartEditWindow.Show(row.Part)) await LoadAsync();
        }, _ => Session.CanEdit);
        ResetPartCommand = new AsyncRelayCommand(ResetPartAsync, _ => Session.CanResetParts);
        AddMaintenanceCommand = new AsyncRelayCommand(async () =>
        {
            if (MaintenanceEditWindow.Show(printerId, printer?.TotalPages)) await LoadAsync();
        }, () => Session.CanEdit);
    }

    public Printer? Printer { get => printer; private set { printer = value; RaisePropertyChanged(); RaisePropertyChanged(nameof(Header)); } }
    public string Header => Printer is null ? "" : $"{Printer.Name} — {Printer.IpAddress}";
    public string BusyText { get => busyText; private set => SetProperty(ref busyText, value); }

    public ObservableCollection<SupplyBarViewModel> Consumables { get; } = new();
    public ObservableCollection<SupplyBarViewModel> OtherSupplies { get; } = new();
    public ObservableCollection<WearPartRowViewModel> WearParts { get; } = new();
    public ObservableCollection<CounterReading> Counters { get; } = new();
    public ObservableCollection<PrinterEvent> Events { get; } = new();
    public ObservableCollection<MaintenanceRecord> Maintenance { get; } = new();
    public ObservableCollection<Alert> Alerts { get; } = new();

    public bool CanEdit => Session.CanEdit;
    public bool HasNoConsumables => Consumables.Count == 0;
    public bool HasNoOtherSupplies => OtherSupplies.Count == 0;

    public ICommand PollNowCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand AddPartCommand { get; }
    public ICommand EditPartCommand { get; }
    public ICommand ResetPartCommand { get; }
    public ICommand AddMaintenanceCommand { get; }

    public async Task LoadAsync()
    {
        var data = await Task.Run(() =>
        {
            var settings = SettingsService.Load();
            var p = PrinterService.Get(printerId, includeDeleted: true);
            using var db = AppDbContext.Create();
            var alerts = db.Alerts.AsNoTracking().Where(a => a.PrinterId == printerId)
                .OrderBy(a => a.ResolvedAt != null).ThenByDescending(a => a.CreatedAt).Take(200).ToList();
            return (settings, p, alerts,
                PrinterService.GetCounterHistory(printerId),
                PrinterService.GetEvents(printerId),
                PrinterService.GetMaintenance(printerId, null, null));
        });

        var (settings, p, alerts, counters, events, maintenance) = data;
        if (p is null) return;
        Printer = p;

        Replace(Consumables, SupplyBarViewModel.FromSupplies(p.Supplies.Where(s => s.Kind is SupplyKind.Toner or SupplyKind.Tinta), settings.CriticalSupplyPercent, true));
        Replace(OtherSupplies, SupplyBarViewModel.FromSupplies(p.Supplies.Where(s => s.Kind is not (SupplyKind.Toner or SupplyKind.Tinta)), settings.CriticalSupplyPercent, false));
        Replace(WearParts, p.WearParts.OrderBy(w => w.Name).Select(w =>
        {
            var wear = w.WearPercent(p.TotalPages);
            return new WearPartRowViewModel
            {
                Part = w,
                UsedPages = w.PagesSinceReset(p.TotalPages),
                WearPercent = wear,
                NeedsReplacement = w.IsActive && wear >= settings.WearAlertPercent
            };
        }));
        RaisePropertyChanged(nameof(HasNoConsumables));
        RaisePropertyChanged(nameof(HasNoOtherSupplies));
        Replace(Counters, counters);
        Replace(Events, events);
        Replace(Maintenance, maintenance);
        Replace(Alerts, alerts);
    }

    private async Task ResetPartAsync(object? parameter)
    {
        if (parameter is not WearPartRowViewModel row) return;
        var notes = TextPromptWindow.Ask("Reset de contador",
            $"Confirme a substituição física de \"{row.Name}\".\nO contador será zerado a partir de {Printer?.TotalPages:N0} páginas.\n\nObservações (opcional):");
        if (notes is null) return;
        await Task.Run(() => PrinterService.ResetWearPart(row.Part.Id, notes));
        await LoadAsync();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}
