using System.Windows;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.Views;

public partial class MaintenanceEditWindow : Window
{
    private readonly int printerId;

    private MaintenanceEditWindow(int printerId, long? currentCounter)
    {
        InitializeComponent();
        this.printerId = printerId;
        DateBox.SelectedDate = DateTime.Today;
        TypeBox.ItemsSource = EnumOption<MaintenanceType>.All;
        TypeBox.SelectedValue = MaintenanceType.Preventiva;
        CounterBox.Text = currentCounter?.ToString() ?? "";
        Loaded += (_, _) => DescriptionBox.Focus();
    }

    public static bool Show(int printerId, long? currentCounter)
    {
        Session.RequireEdit();
        return new MaintenanceEditWindow(printerId, currentCounter) { Owner = Dialogs.Owner() }.ShowDialog() == true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var date = DateBox.SelectedDate ?? DateTime.Today;
            // Mantém a hora atual quando o serviço é registrado no mesmo dia.
            var performed = date.Date == DateTime.Today ? DateTime.Now : date.Date.AddHours(12);
            PrinterService.AddMaintenance(new MaintenanceRecord
            {
                PrinterId = printerId,
                PerformedAt = performed.ToUniversalTime(),
                Type = (MaintenanceType)TypeBox.SelectedValue,
                PartName = string.IsNullOrWhiteSpace(PartBox.Text) ? null : PartBox.Text.Trim(),
                CounterAtService = long.TryParse(CounterBox.Text.Replace(".", ""), out var c) ? c : null,
                Description = DescriptionBox.Text.Trim()
            });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
        }
    }
}
