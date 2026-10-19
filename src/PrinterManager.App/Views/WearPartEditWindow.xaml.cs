using System.Windows;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.Views;

public partial class WearPartEditWindow : Window
{
    private readonly WearPart part;

    private WearPartEditWindow(WearPart part)
    {
        InitializeComponent();
        this.part = part;
        NameBox.Text = part.Name;
        LifeBox.Text = part.NominalLifePages.ToString();
        CounterBox.Text = part.CounterAtLastReset?.ToString() ?? "";
        ActiveBox.IsChecked = part.IsActive;
        Loaded += (_, _) => NameBox.Focus();
    }

    public static bool Show(WearPart part)
    {
        Session.RequireEdit();
        return new WearPartEditWindow(part) { Owner = Dialogs.Owner() }.ShowDialog() == true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!long.TryParse(LifeBox.Text.Replace(".", ""), out var life)) throw new InvalidOperationException("Vida útil inválida.");
            long? counter = null;
            if (!string.IsNullOrWhiteSpace(CounterBox.Text))
                counter = long.TryParse(CounterBox.Text.Replace(".", ""), out var c) ? c : throw new InvalidOperationException("Contador inválido.");

            PrinterService.SaveWearPart(new WearPart
            {
                Id = part.Id,
                PrinterId = part.PrinterId,
                Name = NameBox.Text,
                NominalLifePages = life,
                CounterAtLastReset = counter,
                IsActive = ActiveBox.IsChecked == true
            });
            DialogResult = true;
        }
        catch (Exception ex)
        {
            Dialogs.ShowError(ex);
        }
    }
}
