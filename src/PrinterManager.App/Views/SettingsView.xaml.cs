using System.Windows;
using System.Windows.Controls;
using PrinterManager.App.ViewModels;

namespace PrinterManager.App.Views;

public partial class SettingsView : UserControl
{
    private bool syncing;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is SettingsViewModel old) old.Loaded -= OnSettingsLoaded;
            if (e.NewValue is SettingsViewModel vm)
            {
                vm.Loaded += OnSettingsLoaded;
                OnSettingsLoaded(vm, EventArgs.Empty);
            }
        };
    }

    private void OnSettingsLoaded(object? sender, EventArgs e)
    {
        if (sender is not SettingsViewModel vm) return;
        syncing = true;
        SmtpPasswordBox.Password = vm.SmtpPassword;
        syncing = false;
    }

    private void SmtpPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (!syncing && DataContext is SettingsViewModel vm) vm.SmtpPassword = SmtpPasswordBox.Password;
    }
}
