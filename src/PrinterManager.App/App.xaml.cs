using System.Windows;
using System.Windows.Threading;
using PrinterManager.App.Data;
using PrinterManager.App.Services;
using PrinterManager.App.ViewModels;
using PrinterManager.App.Views;

namespace PrinterManager.App;

public partial class App : Application
{
    private Mutex? singleInstance;
    private bool loggingOut;

    public static PollingService Polling { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();

        // Uma instância por máquina: evita duas varreduras concorrentes sobre o mesmo banco.
        singleInstance = new Mutex(true, @"Global\GestaoImpressoras", out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("O Gestão de Impressoras já está em execução nesta máquina.", "Gestão de Impressoras",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        try
        {
            ThreadPool.SetMinThreads(64, 64);
            // Números e datas das grades no padrão brasileiro (WPF usa en-US por padrão).
            var ptBr = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
            System.Globalization.CultureInfo.DefaultThreadCurrentCulture = ptBr;
            System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = ptBr;
            FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
                new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(ptBr.IetfLanguageTag)));
            QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
            AppPaths.EnsureCreated();
            DatabaseInitializer.Initialize();
        }
        catch (Exception ex)
        {
            FileLog.Error("Falha ao inicializar o banco de dados", ex);
            MessageBox.Show($"Não foi possível abrir o banco de dados em:\n{AppPaths.DatabasePath}\n\n{ex.Message}",
                "Falha ao iniciar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }

        ShowLogin();
    }

    public void ShowLogin()
    {
        var login = new LoginWindow();
        if (login.ShowDialog() != true)
        {
            Shutdown();
            return;
        }

        Polling.Start();
        var main = new MainWindow { DataContext = new MainViewModel(Polling) };
        main.Closed += (_, _) =>
        {
            // Fechar a janela encerra o aplicativo (e o monitoramento); "Sair" volta ao login.
            if (!loggingOut) Shutdown();
        };
        MainWindow = main;
        main.Show();
    }

    /// <summary>Reinicia o aplicativo (após restaurar backup).</summary>
    public void Restart()
    {
        Polling.Dispose();
        singleInstance?.ReleaseMutex();
        singleInstance?.Dispose();
        singleInstance = null;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true });
        Shutdown();
    }

    public void Logout(Window mainWindow)
    {
        AuthService.Logout();
        loggingOut = true;
        mainWindow.Close();
        loggingOut = false;
        ShowLogin();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Polling.Dispose();
        singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FileLog.Error("Excecao nao tratada no AppDomain", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            FileLog.Error("Excecao nao observada em tarefa", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLog.Error("Excecao nao tratada na interface", e.Exception);
        var message = e.Exception is UnauthorizedAccessException or InvalidOperationException
            ? e.Exception.Message
            : $"Ocorreu um erro inesperado:\n{e.Exception.Message}\n\nDetalhes em {AppPaths.LogDirectory}";
        MessageBox.Show(message, "Gestão de Impressoras", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
