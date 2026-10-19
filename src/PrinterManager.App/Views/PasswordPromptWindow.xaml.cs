using System.Windows;

namespace PrinterManager.App.Views;

/// <summary>Confirmação por credenciais de administrador (RN09).</summary>
public partial class PasswordPromptWindow : Window
{
    public PasswordPromptWindow(string title, string message, string userName)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;
        UserBox.Text = userName;
        Loaded += (_, _) => (string.IsNullOrEmpty(userName) ? (UIElement)UserBox : PasswordBox).Focus();
    }

    public string UserName => UserBox.Text;
    public string Password => PasswordBox.Password;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UserBox.Text) || PasswordBox.Password.Length == 0) return;
        DialogResult = true;
    }
}
