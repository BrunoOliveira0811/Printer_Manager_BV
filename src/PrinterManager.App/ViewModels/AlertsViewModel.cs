using System.Collections.ObjectModel;
using System.Windows.Input;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

public sealed class AlertsViewModel : PageViewModel
{
    private bool includeResolved;
    private Alert? selected;

    public AlertsViewModel()
    {
        AcknowledgeCommand = new AsyncRelayCommand(async () =>
        {
            var id = Selected!.Id;
            await Task.Run(() => AlertService.Acknowledge(id));
            await LoadAsync();
        }, () => Selected is { AcknowledgedAt: null, ResolvedAt: null } && Session.CanEdit);
        OpenPrinterCommand = new UiCommand(() => PrinterDetailWindow.Open(Selected!.PrinterId), () => Selected is not null);
        RefreshCommand = new AsyncRelayCommand(LoadAsync);
    }

    public override string Title => "Alertas";

    public ObservableCollection<Alert> Items { get; } = new();

    public bool IncludeResolved
    {
        get => includeResolved;
        set { if (SetProperty(ref includeResolved, value)) _ = LoadAsync(); }
    }

    public Alert? Selected { get => selected; set => SetProperty(ref selected, value); }

    public ICommand AcknowledgeCommand { get; }
    public ICommand OpenPrinterCommand { get; }
    public ICommand RefreshCommand { get; }

    public override Task OnPollCompletedAsync() => LoadAsync();

    public override async Task LoadAsync()
    {
        var include = IncludeResolved;
        var alerts = await Task.Run(() => AlertService.GetAlerts(include));
        Items.Clear();
        foreach (var a in alerts) Items.Add(a);
    }
}
