using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Services;

namespace PrinterManager.App.ViewModels;

/// <summary>Parâmetros globais (somente Administrador — RN03; alterações auditadas — RN14).</summary>
public sealed class SettingsViewModel : PageViewModel
{
    private AppSettingsModel model = new();
    private string status = "";

    public SettingsViewModel()
    {
        SaveCommand = new AsyncRelayCommand(async () =>
        {
            var copy = model.Clone();
            await Task.Run(() => SettingsService.Save(copy));
            Status = $"Configurações salvas às {DateTime.Now:HH:mm:ss}.";
        });
        TestEmailCommand = new AsyncRelayCommand(() => TestAsync("E-mail"));
        TestWebhookCommand = new AsyncRelayCommand(() => TestAsync("Webhook"));
        BackupNowCommand = new AsyncRelayCommand(async () =>
        {
            var copy = model.Clone();
            var path = await Task.Run(() => BackupService.BackupNow(copy));
            Status = $"Backup gerado: {path}";
        });
        RestoreCommand = new UiCommand(Restore);
        BrowseBackupDirCommand = new UiCommand(() =>
        {
            var dialog = new OpenFolderDialog { Title = "Pasta de backups" };
            if (dialog.ShowDialog() == true) { model.BackupDirectory = dialog.FolderName; RaisePropertyChanged(nameof(Settings)); }
        });
        OpenDataFolderCommand = new UiCommand(() => Process.Start(new ProcessStartInfo(AppPaths.DataDirectory) { UseShellExecute = true }));
    }

    public override string Title => "Configurações";

    public AppSettingsModel Settings => model;
    public string DataDirectory => AppPaths.DataDirectory;
    public int MinPollInterval => AppSettingsModel.MinPollIntervalMinutes;

    public string Status { get => status; private set => SetProperty(ref status, value); }

    /// <summary>Preenchido pela view a partir do PasswordBox.</summary>
    public string SmtpPassword { get => model.SmtpPassword; set => model.SmtpPassword = value; }

    public ICommand SaveCommand { get; }
    public ICommand TestEmailCommand { get; }
    public ICommand TestWebhookCommand { get; }
    public ICommand BackupNowCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand BrowseBackupDirCommand { get; }
    public ICommand OpenDataFolderCommand { get; }

    public event EventHandler? Loaded;

    public override async Task LoadAsync()
    {
        Session.RequireAdmin();
        model = await Task.Run(SettingsService.Load);
        RaisePropertyChanged(nameof(Settings));
        Loaded?.Invoke(this, EventArgs.Empty);
    }

    private async Task TestAsync(string channel)
    {
        Status = $"Enviando teste via {channel}…";
        try
        {
            var copy = model.Clone();
            await NotificationService.SendTestAsync(copy, channel);
            Status = $"Teste via {channel} enviado com sucesso.";
        }
        catch (Exception ex)
        {
            Status = $"Falha no teste via {channel}: {ex.Message}";
        }
    }

    private void Restore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Selecione o backup a restaurar",
            Filter = "Banco de dados (*.db)|*.db",
            InitialDirectory = model.EffectiveBackupDirectory
        };
        if (dialog.ShowDialog() != true) return;
        if (!Dialogs.Confirm("O banco atual será substituído pelo backup selecionado (uma cópia de segurança do banco atual é feita antes).\n\n" +
                             "O aplicativo será reiniciado. Deseja continuar?")) return;

        App.Polling.Dispose();
        BackupService.Restore(dialog.FileName);
        ((App)Application.Current).Restart();
    }
}
