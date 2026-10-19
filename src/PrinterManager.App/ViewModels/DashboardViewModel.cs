using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using PrinterManager.App.Data;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

public sealed class PrinterCardViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string Sector { get; init; } = "";
    public string Model { get; init; } = "";
    public ConnectionStatus Status { get; init; }
    public string LastPoll { get; init; } = "";
    public string Counter { get; init; } = "";
    public string? WorstWear { get; init; }
    public bool WearWarning { get; init; }
    public int AlertCount { get; init; }
    public List<SupplyBarViewModel> Supplies { get; init; } = new();
    public bool HasNoSupplies => Supplies.Count == 0;
    public string AccessibleName => $"{Name}, {IpAddress}, estado {Status.GetDescription()}, {AlertCount} alerta(s)";
}

/// <summary>Painel centralizado de saúde (RF06). Lê apenas o banco, sem SNMP, para abrir em menos de 3 s (RNF02).</summary>
public sealed class DashboardViewModel(MainViewModel shell) : PageViewModel
{
    private int total, online, offline, withAlerts, licensed;
    private string sectorFilter = "Todos";

    public override string Title => "Painel de controle";

    public ObservableCollection<PrinterCardViewModel> Cards { get; } = new();
    public ObservableCollection<string> Sectors { get; } = new() { "Todos" };

    public int Total { get => total; private set => SetProperty(ref total, value); }
    public int Online { get => online; private set => SetProperty(ref online, value); }
    public int Offline { get => offline; private set => SetProperty(ref offline, value); }
    public int WithAlerts { get => withAlerts; private set => SetProperty(ref withAlerts, value); }
    /// <summary>RN13: somente impressoras ativas consomem licença.</summary>
    public int Licensed { get => licensed; private set => SetProperty(ref licensed, value); }
    public MainViewModel Shell => shell;

    public string SectorFilter
    {
        get => sectorFilter;
        set { if (SetProperty(ref sectorFilter, value)) _ = LoadAsync(); }
    }

    private ICommand? openPrinterCommand, refreshCommand;

    public ICommand OpenPrinterCommand => openPrinterCommand ??= new UiCommand(p =>
    {
        if (p is PrinterCardViewModel card) PrinterDetailWindow.Open(card.Id);
    });

    public ICommand RefreshCommand => refreshCommand ??= new AsyncRelayCommand(LoadAsync);

    public override Task OnPollCompletedAsync() => LoadAsync();

    public override async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var sector = SectorFilter;
            var data = await Task.Run(() =>
            {
                var settings = SettingsService.Load();
                using var db = AppDbContext.Create();
                var printers = db.Printers.AsNoTracking()
                    .Include(p => p.Supplies).Include(p => p.WearParts)
                    .Where(p => !p.IsDeleted && p.Lifecycle == LifecycleStatus.Ativa)
                    .AsSplitQuery()
                    .ToList();
                var alertCounts = AlertService.ActiveAlertsQuery(db)
                    .GroupBy(a => a.PrinterId).Select(g => new { g.Key, Count = g.Count() })
                    .ToDictionary(g => g.Key, g => g.Count);
                var sectors = printers.Select(p => p.Sector).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().OrderBy(s => s).ToList();
                return (settings, printers, alertCounts, sectors);
            });

            var (settings, printers, alertCounts, sectors) = data;

            Licensed = printers.Count;
            var visible = sector == "Todos" ? printers : printers.Where(p => p.Sector == sector).ToList();
            Total = visible.Count;
            Online = visible.Count(p => p.Status is ConnectionStatus.Online or ConnectionStatus.Alerta or ConnectionStatus.Erro);
            Offline = visible.Count(p => p.Status == ConnectionStatus.Offline);
            WithAlerts = visible.Count(p => alertCounts.ContainsKey(p.Id));

            foreach (var s in sectors.Where(s => !Sectors.Contains(s!))) Sectors.Add(s!);

            Cards.Clear();
            // Problemas primeiro: Offline/Erro/Alerta, depois por nome.
            foreach (var p in visible.OrderByDescending(p => alertCounts.GetValueOrDefault(p.Id) > 0 || p.Status is ConnectionStatus.Offline or ConnectionStatus.Erro)
                         .ThenByDescending(p => p.Status).ThenBy(p => p.Name))
            {
                var worst = p.WearParts.Where(w => w.IsActive)
                    .Select(w => (w.Name, Wear: w.WearPercent(p.TotalPages)))
                    .Where(w => w.Wear is not null)
                    .OrderByDescending(w => w.Wear)
                    .FirstOrDefault();

                Cards.Add(new PrinterCardViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    IpAddress = p.IpAddress,
                    Sector = p.Sector ?? "Sem setor",
                    Model = p.Model ?? "",
                    Status = p.Status,
                    LastPoll = p.LastSuccessAt is { } t ? $"Atualizada {t.ToLocalTime():dd/MM HH:mm}"
                        : p.LastPollAt is null ? "Ainda não consultada" : "Nunca respondeu",
                    Counter = p.TotalPages is { } c ? $"{c:N0} páginas" : "",
                    WorstWear = worst.Name is null ? null : $"{worst.Name}: {worst.Wear:0}% de desgaste",
                    WearWarning = worst.Wear >= settings.WearAlertPercent,
                    AlertCount = alertCounts.GetValueOrDefault(p.Id),
                    Supplies = SupplyBarViewModel.FromSupplies(p.Supplies, settings.CriticalSupplyPercent, onlyConsumables: true)
                });
            }
        });
    }
}
