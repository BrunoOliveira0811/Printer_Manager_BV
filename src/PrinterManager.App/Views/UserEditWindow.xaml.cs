using System.Windows;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.Views;

public partial class UserEditWindow : Window
{
    private readonly User? user;

    private UserEditWindow(User? user)
    {
        InitializeComponent();
        this.user = user;
        RoleBox.ItemsSource = EnumOption<UserRole>.All;
        RoleBox.SelectedValue = user?.Role ?? UserRole.Visualizador;

        if (user is not null)
        {
            Title = $"Editar usuário — {user.UserName}";
            UserNameBox.Text = user.UserName;
            UserNameBox.IsReadOnly = true;
            FullNameBox.Text = user.FullName;
            EmailBox.Text = user.Email;
            ActiveBox.IsChecked = user.IsActive;
            PasswordPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            Title = "Novo usuário";
            ActiveBox.Visibility = Visibility.Collapsed;
        }
        Loaded += (_, _) => (user is null ? UserNameBox : FullNameBox).Focus();
    }

    public static bool Show(User? user)
    {
        Session.RequireAdmin();
        return new UserEditWindow(user) { Owner = Dialogs.Owner() }.ShowDialog() == true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var role = (UserRole)RoleBox.SelectedValue;
            if (user is null)
            {
                if (PasswordBox.Password != ConfirmBox.Password) throw new InvalidOperationException("As senhas não conferem.");
                AuthService.CreateUser(UserNameBox.Text, FullNameBox.Text, EmailBox.Text, role, PasswordBox.Password);
            }
            else
            {
                AuthService.UpdateUser(user.Id, FullNameBox.Text, EmailBox.Text, role, ActiveBox.IsChecked == true);
            }
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
        }
    }
}
