using System.Windows;
using PrinterManager.App.Services;

namespace PrinterManager.App.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow() => InitializeComponent();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (NewBox.Password != ConfirmBox.Password) throw new InvalidOperationException("As senhas não conferem.");
            var (current, next) = (CurrentBox.Password, NewBox.Password);
            await Task.Run(() => AuthService.ChangeOwnPassword(current, next));
            MessageBox.Show(this, "Senha alterada com sucesso.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (InvalidOperationException ex)
        {
            ErrorText.Text = ex.Message;
        }
    }
}
