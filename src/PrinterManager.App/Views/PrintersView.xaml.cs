using System.Windows.Controls;
using System.Windows.Input;
using PrinterManager.App.ViewModels;

namespace PrinterManager.App.Views;

public partial class PrintersView : UserControl
{
    public PrintersView() => InitializeComponent();

    private void Grid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is PrintersViewModel vm && vm.DetailsCommand.CanExecute(null)) vm.DetailsCommand.Execute(null);
    }
}
