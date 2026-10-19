using System.Windows;
using System.Windows.Controls;
using PrinterManager.App.ViewModels;

namespace PrinterManager.App.Views;

public partial class DiscoveryView : UserControl
{
    public DiscoveryView() => InitializeComponent();

    private void AuthPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscoveryViewModel vm) vm.V3AuthPassword = AuthPassword.Password;
    }

    private void PrivPassword_Changed(object sender, RoutedEventArgs e)
    {
        if (DataContext is DiscoveryViewModel vm) vm.V3PrivPassword = PrivPassword.Password;
    }
}
