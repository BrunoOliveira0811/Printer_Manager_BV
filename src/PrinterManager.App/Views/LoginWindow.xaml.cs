using System.Windows;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.Views;

public partial class LoginWindow : Window
{
    private readonly bool firstRun;

    public LoginWindow()
    {
        InitializeComponent();
        firstRun = !AuthService.HasAnyUser();
        if (firstRun)
        {
            SetupPanel.Visibility = Visibility.Visible;
            ConfirmPanel.Visibility = Visibility.Visible;
            LoginButton.Content = "Criar administrador";
            UserNameBox.Text = "admin";
        }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var userName = UserNameBox.Text;
        var password = PasswordBox.Password;
        LoginButton.IsEnabled = false;
        try
        {
            if (firstRun)
            {
                if (password != ConfirmBox.Password) throw new InvalidOperationException("As senhas não conferem.");
                var fullName = string.IsNullOrWhiteSpace(FullNameBox.Text) ? "Administrador" : FullNameBox.Text;
                await Task.Run(() => AuthService.CreateUser(userName, fullName, null, UserRole.Administrador, password));
            }

            // bcrypt é propositalmente lento; roda fora da thread da interface.
            var result = await Task.Run(() => AuthService.Login(userName, password));
            if (!result.Success) throw new InvalidOperationException(result.Message);
            DialogResult = true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            PasswordBox.Clear();
            PasswordBox.Focus();
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
