using System.Windows;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.ViewModels;

namespace PrinterManager.App.Views;

public partial class PrinterDetailWindow : Window
{
    private static readonly Dictionary<int, PrinterDetailWindow> OpenWindows = new();

    private PrinterDetailWindow(int printerId)
    {
        InitializeComponent();
        var vm = new PrinterDetailViewModel(printerId, App.Polling);
        DataContext = vm;
        Loaded += async (_, _) =>
        {
            try { await vm.LoadAsync(); }
            catch (Exception ex) { Dialogs.ShowError(ex); }
        };
        Closed += (_, _) => OpenWindows.Remove(printerId);
    }

    /// <summary>Abre (ou traz para frente) a janela de detalhes da impressora.</summary>
    public static void Open(int printerId)
    {
        if (OpenWindows.TryGetValue(printerId, out var existing))
        {
            existing.Activate();
            return;
        }
        var window = new PrinterDetailWindow(printerId) { Owner = Application.Current.MainWindow };
        OpenWindows[printerId] = window;
        window.Show();
    }
}
