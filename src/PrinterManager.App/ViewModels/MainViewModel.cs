using System.Collections.ObjectModel;
using System.Media;
using System.Windows;
using System.Windows.Input;
using PrinterManager.App.Data;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

public sealed record NavItem(string Title, string Glyph, Func<PageViewModel> Factory);

public sealed class MainViewModel : ObservableObject
{
    private readonly PollingService polling;
    private readonly Dictionary<string, PageViewModel> pages = new();
    private NavItem? selectedNav;
    private PageViewModel? currentPage;
    private int criticalAlertCount;
    private int warningAlertCount;
    private string latestCriticalMessage = "";
    private string pollingStatus = "";
    private bool flashAlert;

    public MainViewModel(PollingService polling)
    {
        this.polling = polling;

        var items = new List<NavItem>
        {
            new("Painel", "", () => new DashboardViewModel(this)),
            new("Impressoras", "", () => new PrintersViewModel(this)),
            new("Descoberta na rede", "", () => new DiscoveryViewModel()),
            new("Alertas", "", () => new AlertsViewModel()),
            new("Manutenções", "", () => new MaintenanceViewModel()),
            new("Relatórios", "", () => new ReportsViewModel()),
        };
        if (Session.IsAdmin)
        {
            // RN03: configurações globais e usuários somente para Administrador.
            items.Add(new NavItem("Auditoria", "", () => new AuditViewModel()));
            items.Add(new NavItem("Usuários", "", () => new UsersViewModel()));
            items.Add(new NavItem("Configurações", "", () => new SettingsViewModel()));
        }
        NavItems = new ObservableCollection<NavItem>(items);

        RefreshAllCommand = new AsyncRelayCommand(RefreshAllAsync, () => !polling.IsBusy);
        GoToAlertsCommand = new UiCommand(() => Navigate("Alertas"));
        LogoutCommand = new UiCommand(_ =>
        {
            if (Application.Current.MainWindow is { } w) ((App)Application.Current).Logout(w);
        });
        ChangePasswordCommand = new UiCommand(() => new ChangePasswordWindow { Owner = Application.Current.MainWindow }.ShowDialog());

        polling.PollCompleted += OnPollCompleted;
        polling.StateChanged += OnPollingStateChanged;

        SelectedNav = NavItems[0];
        _ = RefreshAlertSummaryAsync(playSound: false);
        UpdatePollingStatus();
    }

    public ObservableCollection<NavItem> NavItems { get; }

    public string UserDisplay => $"{Session.CurrentUser?.FullName} ({Session.CurrentUser?.Role.GetDescription()})";

    public NavItem? SelectedNav
    {
        get => selectedNav;
        set
        {
            if (!SetProperty(ref selectedNav, value) || value is null) return;
            if (!pages.TryGetValue(value.Title, out var page))
                pages[value.Title] = page = value.Factory();
            CurrentPage = page;
            _ = LoadPageAsync(page);
        }
    }

    public PageViewModel? CurrentPage
    {
        get => currentPage;
        private set => SetProperty(ref currentPage, value);
    }

    public int CriticalAlertCount
    {
        get => criticalAlertCount;
        private set { if (SetProperty(ref criticalAlertCount, value)) RaisePropertyChanged(nameof(HasCriticalAlerts)); }
    }

    public int WarningAlertCount
    {
        get => warningAlertCount;
        private set => SetProperty(ref warningAlertCount, value);
    }

    public bool HasCriticalAlerts => CriticalAlertCount > 0;

    public string LatestCriticalMessage
    {
        get => latestCriticalMessage;
        private set => SetProperty(ref latestCriticalMessage, value);
    }

    /// <summary>Alterna para chamar atenção quando surge um novo alerta crítico (RF07).</summary>
    public bool FlashAlert
    {
        get => flashAlert;
        private set => SetProperty(ref flashAlert, value);
    }

    public string PollingStatus
    {
        get => pollingStatus;
        private set => SetProperty(ref pollingStatus, value);
    }

    public ICommand RefreshAllCommand { get; }
    public ICommand GoToAlertsCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ChangePasswordCommand { get; }

    public PollingService Polling => polling;

    /// <summary>Desliga os eventos do agendador ao fechar a janela (logout).</summary>
    public void Detach()
    {
        polling.PollCompleted -= OnPollCompleted;
        polling.StateChanged -= OnPollingStateChanged;
    }

    public void Navigate(string title)
    {
        var item = NavItems.FirstOrDefault(n => n.Title == title);
        if (item is not null) SelectedNav = item;
    }

    private static async Task LoadPageAsync(PageViewModel page)
    {
        try { await page.LoadAsync(); }
        catch (Exception ex) { Dialogs.ShowError(ex); }
    }

    private async Task RefreshAllAsync()
    {
        List<int> ids;
        using (var db = AppDbContext.Create())
            ids = db.Printers.Where(p => !p.IsDeleted && p.Lifecycle == LifecycleStatus.Ativa).Select(p => p.Id).ToList();
        if (ids.Count == 0)
        {
            Dialogs.Info("Nenhuma impressora ativa cadastrada.");
            return;
        }
        await Task.Run(() => polling.PollNowAsync(ids));
    }

    private void OnPollingStateChanged(object? sender, EventArgs e) =>
        Application.Current?.Dispatcher.BeginInvoke(UpdatePollingStatus);

    private void UpdatePollingStatus()
    {
        var last = polling.LastCycleAt is { } t ? t.ToLocalTime().ToString("HH:mm") : "—";
        PollingStatus = polling.IsBusy
            ? $"Consultando {polling.InFlightCount} impressora(s)…"
            : $"Agendador ativo · última verificação {last}";
        CommandManager.InvalidateRequerySuggested();
    }

    private void OnPollCompleted(object? sender, PollCompletedEventArgs e)
    {
        // BeginInvoke evita reentrância no dispatcher durante diálogos modais.
        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            var newCritical = e.NewAlerts.Any(a => a.Severity == AlertSeverity.Critico);
            await RefreshAlertSummaryAsync(newCritical);
            if (CurrentPage is not null)
            {
                try { await CurrentPage.OnPollCompletedAsync(); }
                catch (Exception ex) { FileLog.Warn("Falha ao atualizar pagina apos varredura", ex); }
            }
        });
    }

    public async Task RefreshAlertSummaryAsync(bool playSound)
    {
        var (critical, warning, latest) = await Task.Run(() =>
        {
            using var db = AppDbContext.Create();
            var active = AlertService.ActiveAlertsQuery(db);
            var last = active.Where(a => a.Severity == AlertSeverity.Critico)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => a.Printer!.Name + ": " + a.Message)
                .FirstOrDefault();
            return (active.Count(a => a.Severity == AlertSeverity.Critico), active.Count(a => a.Severity == AlertSeverity.Aviso), last);
        });

        CriticalAlertCount = critical;
        WarningAlertCount = warning;
        LatestCriticalMessage = latest ?? "";

        if (playSound && critical > 0)
        {
            FlashAlert = !FlashAlert;
            if (SettingsService.Load().SoundAlerts) SystemSounds.Hand.Play();
        }
    }
}
