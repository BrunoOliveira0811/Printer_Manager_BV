using System.Collections.ObjectModel;
using System.Windows.Input;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

public sealed class PrinterRowViewModel(Printer p, int criticalPercent)
{
    public Printer Printer { get; } = p;
    public int Id => Printer.Id;
    public string Name => Printer.Name;
    public string IpAddress => Printer.IpAddress;
    public string? Sector => Printer.Sector;
    public string? Model => Printer.Model;
    public string? Location => Printer.Location;
    public ConnectionStatus Status => Printer.Status;
    public LifecycleStatus Lifecycle => Printer.Lifecycle;
    public string Snmp => Printer.SnmpVersion.GetDescription();
    public long? TotalPages => Printer.TotalPages;
    public DateTime? LastSuccessAt => Printer.LastSuccessAt;

    public string SupplySummary => string.Join("  ",
        Printer.Supplies.Where(s => s.Kind is SupplyKind.Toner or SupplyKind.Tinta)
            .OrderBy(s => s.Color)
            .Select(s => $"{ColorLetter(s.Color)}:{(s.Percent is { } v ? $"{v:0}%" : "—")}{(s.Percent <= criticalPercent ? "!" : "")}"));

    private static string ColorLetter(SupplyColor c) => c switch
    {
        SupplyColor.Preto => "K", SupplyColor.Ciano => "C", SupplyColor.Magenta => "M", SupplyColor.Amarelo => "Y", _ => "?"
    };
}

/// <summary>Lista com busca e filtros por status, setor, modelo e IP (RF15).</summary>
public sealed class PrintersViewModel : PageViewModel
{
    private const string All = "Todos";
    private readonly MainViewModel shell;
    private string searchText = "";
    private string selectedStatus = All;
    private string selectedLifecycle = EnumOption<LifecycleStatus>.All[0].Text;
    private string selectedSector = All;
    private string selectedModel = All;
    private PrinterRowViewModel? selected;
    private CancellationTokenSource? searchDebounce;

    public PrintersViewModel(MainViewModel shell)
    {
        this.shell = shell;
        StatusOptions = new[] { All }.Concat(EnumOption<ConnectionStatus>.All.Select(o => o.Text)).ToList();
        LifecycleOptions = new[] { All }.Concat(EnumOption<LifecycleStatus>.All.Select(o => o.Text)).ToList();

        AddCommand = new AsyncRelayCommand(async () =>
        {
            if (PrinterEditWindow.ShowNew()) await LoadAsync();
        }, () => Session.CanEdit);
        EditCommand = new AsyncRelayCommand(async () =>
        {
            if (PrinterEditWindow.ShowEdit(Selected!.Id)) await LoadAsync();
        }, () => Selected is not null && Session.CanEdit);
        DetailsCommand = new UiCommand(() => PrinterDetailWindow.Open(Selected!.Id), () => Selected is not null);
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => Selected is not null && Session.CanEdit);
        PollSelectedCommand = new AsyncRelayCommand(async () =>
        {
            var id = Selected!.Id;
            await Task.Run(() => shell.Polling.PollNowAsync([id]));
            await LoadAsync();
        }, () => Selected is not null);
        ClearFiltersCommand = new AsyncRelayCommand(async () =>
        {
            searchText = ""; selectedStatus = All; selectedSector = All; selectedModel = All;
            selectedLifecycle = LifecycleOptions[1];
            RaisePropertyChanged(nameof(SearchText)); RaisePropertyChanged(nameof(SelectedStatus));
            RaisePropertyChanged(nameof(SelectedSector)); RaisePropertyChanged(nameof(SelectedModel));
            RaisePropertyChanged(nameof(SelectedLifecycle));
            await LoadAsync();
        });
        selectedLifecycle = LifecycleOptions[1]; // "Ativa"
    }

    public override string Title => "Impressoras";

    public ObservableCollection<PrinterRowViewModel> Items { get; } = new();
    public IReadOnlyList<string> StatusOptions { get; }
    public IReadOnlyList<string> LifecycleOptions { get; }
    public ObservableCollection<string> SectorOptions { get; } = new() { All };
    public ObservableCollection<string> ModelOptions { get; } = new() { All };

    public string SearchText
    {
        get => searchText;
        set { if (SetProperty(ref searchText, value)) DebouncedReload(); }
    }

    public string SelectedStatus { get => selectedStatus; set { if (SetProperty(ref selectedStatus, value)) _ = LoadAsync(); } }
    public string SelectedLifecycle { get => selectedLifecycle; set { if (SetProperty(ref selectedLifecycle, value)) _ = LoadAsync(); } }
    public string SelectedSector { get => selectedSector; set { if (SetProperty(ref selectedSector, value ?? All)) _ = LoadAsync(); } }
    public string SelectedModel { get => selectedModel; set { if (SetProperty(ref selectedModel, value ?? All)) _ = LoadAsync(); } }

    public PrinterRowViewModel? Selected { get => selected; set => SetProperty(ref selected, value); }

    public string CountText => $"{Items.Count} impressora(s)";

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand DetailsCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand PollSelectedCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public override Task OnPollCompletedAsync() => LoadAsync();

    public override async Task LoadAsync()
    {
        var filter = new PrinterFilter
        {
            Text = SearchText,
            Status = ParseEnum<ConnectionStatus>(SelectedStatus),
            Lifecycle = ParseEnum<LifecycleStatus>(SelectedLifecycle),
            Sector = SelectedSector == All ? null : SelectedSector,
            Model = SelectedModel == All ? null : SelectedModel
        };

        await RunBusyAsync(async () =>
        {
            var (printers, options, critical) = await Task.Run(() =>
                (PrinterService.Search(filter), PrinterService.GetFilterOptions(), SettingsService.Load().CriticalSupplyPercent));

            var selectedId = Selected?.Id;
            Items.Clear();
            foreach (var p in printers) Items.Add(new PrinterRowViewModel(p, critical));
            Selected = Items.FirstOrDefault(i => i.Id == selectedId);
            RaisePropertyChanged(nameof(CountText));

            Sync(SectorOptions, options.Sectors);
            Sync(ModelOptions, options.Models);
        });
    }

    private async Task DeleteAsync()
    {
        var row = Selected!;
        var prompt = new PasswordPromptWindow(
            "Excluir impressora",
            $"A exclusão de \"{row.Name}\" ({row.IpAddress}) exige a senha de um Administrador.\n" +
            "O histórico de contadores, manutenções e relatórios será preservado.",
            Session.IsAdmin ? Session.CurrentUser!.UserName : "")
        { Owner = Dialogs.Owner() };

        if (prompt.ShowDialog() != true) return;
        await Task.Run(() => PrinterService.SoftDelete(row.Id, prompt.UserName, prompt.Password));
        await LoadAsync();
        await shell.RefreshAlertSummaryAsync(false);
    }

    private void DebouncedReload()
    {
        searchDebounce?.Cancel();
        searchDebounce = new CancellationTokenSource();
        var token = searchDebounce.Token;
        _ = Task.Delay(300, token).ContinueWith(_ => LoadAsync(), token,
            TaskContinuationOptions.OnlyOnRanToCompletion, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static T? ParseEnum<T>(string text) where T : struct, Enum =>
        EnumOption<T>.All.FirstOrDefault(o => o.Text == text) is { } option ? option.Value : null;

    private static void Sync(ObservableCollection<string> target, List<string> values)
    {
        foreach (var v in values.Where(v => !target.Contains(v))) target.Add(v);
    }
}
