using System.Collections.ObjectModel;
using System.Windows.Input;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;
using PrinterManager.App.Views;

namespace PrinterManager.App.ViewModels;

/// <summary>Gestão de usuários e perfis — exclusiva do Administrador (RF11, RN03).</summary>
public sealed class UsersViewModel : PageViewModel
{
    private User? selected;

    public UsersViewModel()
    {
        AddCommand = new AsyncRelayCommand(async () =>
        {
            if (UserEditWindow.Show(null)) await LoadAsync();
        });
        EditCommand = new AsyncRelayCommand(async () =>
        {
            if (UserEditWindow.Show(Selected)) await LoadAsync();
        }, () => Selected is not null);
        ResetPasswordCommand = new AsyncRelayCommand(async () =>
        {
            var user = Selected!;
            var password = TextPromptWindow.AskPassword("Redefinir senha", $"Nova senha para {user.UserName}:");
            if (password is null) return;
            await Task.Run(() => AuthService.ResetPassword(user.Id, password));
            Dialogs.Info("Senha redefinida.");
        }, () => Selected is not null);
        UnlockCommand = new AsyncRelayCommand(async () =>
        {
            var id = Selected!.Id;
            await Task.Run(() => AuthService.Unlock(id));
            await LoadAsync();
        }, () => Selected?.LockoutUntil is { } until && until > DateTime.UtcNow);
    }

    public override string Title => "Usuários";

    public ObservableCollection<User> Items { get; } = new();
    public User? Selected { get => selected; set => SetProperty(ref selected, value); }

    public ICommand AddCommand { get; }
    public ICommand EditCommand { get; }
    public ICommand ResetPasswordCommand { get; }
    public ICommand UnlockCommand { get; }

    public override async Task LoadAsync()
    {
        var users = await Task.Run(AuthService.GetUsers);
        Items.Clear();
        foreach (var u in users) Items.Add(u);
    }
}
