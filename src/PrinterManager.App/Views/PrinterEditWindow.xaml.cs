using System.Windows;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Services;
using PrinterManager.App.ViewModels;

namespace PrinterManager.App.Views;

public partial class PrinterEditWindow : Window
{
    private readonly PrinterEditViewModel vm;

    private PrinterEditWindow(PrinterEditViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        DataContext = vm;
        Loaded += (_, _) => IpBox.Focus();
    }

    public static bool ShowNew() => ShowDialogFor(new PrinterEditViewModel(null, SettingsService.Load().DefaultCommunity));

    public static bool ShowEdit(int printerId)
    {
        var printer = PrinterService.Get(printerId) ?? throw new InvalidOperationException("Impressora não encontrada.");
        return ShowDialogFor(new PrinterEditViewModel(printer, printer.Community));
    }

    private static bool ShowDialogFor(PrinterEditViewModel vm)
    {
        Session.RequireEdit();
        return new PrinterEditWindow(vm) { Owner = Dialogs.Owner() }.ShowDialog() == true;
    }

    private void CopyPasswords()
    {
        // PasswordBox vazio = manter a senha armazenada (nulo).
        vm.V3AuthPassword = AuthPasswordBox.Password.Length > 0 ? AuthPasswordBox.Password : null;
        vm.V3PrivPassword = PrivPasswordBox.Password.Length > 0 ? PrivPasswordBox.Password : null;
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        var button = (System.Windows.Controls.Button)sender;
        button.IsEnabled = false;
        CopyPasswords();
        try { await vm.TestAsync(); }
        finally { button.IsEnabled = true; }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        CopyPasswords();
        try
        {
            vm.Save();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
        }
    }
}
