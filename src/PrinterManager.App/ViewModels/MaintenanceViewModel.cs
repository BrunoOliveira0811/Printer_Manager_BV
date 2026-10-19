using System.Collections.ObjectModel;
using System.Windows.Input;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

/// <summary>Histórico de manutenções e substituições de peças (RF10).</summary>
public sealed class MaintenanceViewModel : PageViewModel
{
    private DateTime from = DateTime.Today.AddMonths(-3);
    private DateTime to = DateTime.Today;
    private Printer? selectedPrinter;

    public MaintenanceViewModel()
    {
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
        AddCommand = new AsyncRelayCommand(async () =>
        {
            var printer = SelectedPrinter;
            if (printer is null || printer.Id == 0)
            {
                Dialogs.Info("Selecione a impressora no filtro para registrar uma manutenção.");
                return;
            }
            if (MaintenanceEditWindow.Show(printer.Id, printer.TotalPages)) await LoadAsync();
        }, () => Session.CanEdit);
    }

    public override string Title => "Manutenções";

    public ObservableCollection<MaintenanceRecord> Items { get; } = new();
    public ObservableCollection<Printer> Printers { get; } = new();

    public DateTime From { get => from; set => SetProperty(ref from, value); }
    public DateTime To { get => to; set => SetProperty(ref to, value); }

    public Printer? SelectedPrinter
    {
        get => selectedPrinter;
        set { if (SetProperty(ref selectedPrinter, value)) _ = LoadAsync(); }
    }

    public ICommand RefreshCommand { get; }
    public ICommand AddCommand { get; }

    public override async Task LoadAsync()
    {
        if (Printers.Count == 0)
        {
            var printers = await Task.Run(PrinterService.GetAllIncludingDeleted);
            Printers.Add(new Printer { Id = 0, Name = "Todas as impressoras" });
            foreach (var p in printers) Printers.Add(p);
            selectedPrinter = Printers[0];
            RaisePropertyChanged(nameof(SelectedPrinter));
        }

        int? printerId = SelectedPrinter is { Id: > 0 } sp ? sp.Id : null;
        var fromUtc = From.Date.ToUniversalTime();
        var toUtc = To.Date.AddDays(1).ToUniversalTime();
        var records = await Task.Run(() => PrinterService.GetMaintenance(printerId, fromUtc, toUtc));
        Items.Clear();
        foreach (var r in records) Items.Add(r);
    }
}
