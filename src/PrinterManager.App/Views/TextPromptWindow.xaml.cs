using System.Windows;
using System.Windows.Automation;
using PrinterManager.App.Infrastructure;

namespace PrinterManager.App.Views;

public partial class TextPromptWindow : Window
{
    private readonly bool passwordMode;

    private TextPromptWindow(string title, string message, bool passwordMode)
    {
        InitializeComponent();
        this.passwordMode = passwordMode;
        Title = title;
        MessageText.Text = message;
        AutomationProperties.SetName(InputBox, message);
        AutomationProperties.SetName(PasswordInput, message);
        AutomationProperties.SetName(PasswordConfirm, "Confirmar senha");
        if (passwordMode)
        {
            InputBox.Visibility = Visibility.Collapsed;
            PasswordInput.Visibility = Visibility.Visible;
            PasswordConfirm.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => (passwordMode ? (UIElement)PasswordInput : InputBox).Focus();
    }

    /// <summary>Retorna o texto digitado ou nulo se cancelado.</summary>
    public static string? Ask(string title, string message)
    {
        var w = new TextPromptWindow(title, message, false) { Owner = Dialogs.Owner() };
        return w.ShowDialog() == true ? w.InputBox.Text : null;
    }

    public static string? AskPassword(string title, string message)
    {
        var w = new TextPromptWindow(title, message, true) { Owner = Dialogs.Owner() };
        return w.ShowDialog() == true ? w.PasswordInput.Password : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (passwordMode && PasswordInput.Password != PasswordConfirm.Password)
        {
            ErrorText.Text = "As senhas não conferem.";
            return;
        }
        DialogResult = true;
    }
}
