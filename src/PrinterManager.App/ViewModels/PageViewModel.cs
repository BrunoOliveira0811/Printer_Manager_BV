using System.Windows.Media;
using PrinterManager.App.Infrastructure;
using PrinterManager.App.Models;
using PrinterManager.App.Services;

namespace PrinterManager.App.ViewModels;

public abstract class PageViewModel : ObservableObject
{
    private bool isBusy;

    public abstract string Title { get; }

    public bool IsBusy
    {
        get => isBusy;
        protected set => SetProperty(ref isBusy, value);
    }

    public bool CanEdit => Session.CanEdit;
    public bool IsAdmin => Session.IsAdmin;

    public abstract Task LoadAsync();

    /// <summary>Chamado quando uma varredura termina; páginas que exibem estado ao vivo recarregam.</summary>
    public virtual Task OnPollCompletedAsync() => Task.CompletedTask;

    protected async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        try { await action(); }
        finally { IsBusy = false; }
    }
}

/// <summary>Barra de nível usada no dashboard e nos detalhes (RF03/RF04).</summary>
public sealed class SupplyBarViewModel
{
    public string Label { get; init; } = "";
    public string Description { get; init; } = "";
    public double Percent { get; init; }
    public string PercentText { get; init; } = "";
    public Brush Brush { get; init; } = Brushes.Gray;
    public bool IsCritical { get; init; }
    public string AccessibleText => $"{Description}: {PercentText}{(IsCritical ? " — nível crítico" : "")}";

    public static List<SupplyBarViewModel> FromSupplies(IEnumerable<Supply> supplies, int criticalPercent, bool onlyConsumables)
    {
        return supplies
            .Where(s => !onlyConsumables || s.Kind is SupplyKind.Toner or SupplyKind.Tinta)
            .OrderBy(s => s.Kind is SupplyKind.Toner or SupplyKind.Tinta ? 0 : 1)
            .ThenBy(s => s.Color)
            .ThenBy(s => s.SnmpIndex)
            .Select(s => new SupplyBarViewModel
            {
                Label = s.Kind is SupplyKind.Toner or SupplyKind.Tinta && s.Color != SupplyColor.Outro
                    ? s.Color.GetDescription()
                    : s.Description,
                Description = s.Description,
                Percent = s.Percent ?? (s.Level == -3 ? 100 : 0),
                PercentText = s.Percent is { } p ? $"{p:0}%" : s.Level == -3 ? "OK" : "—",
                Brush = s.Kind is SupplyKind.Toner or SupplyKind.Tinta
                    ? SupplyColorToBrushConverter.ToBrush(s.Color)
                    : SupplyColorToBrushConverter.ToBrush(SupplyColor.Outro),
                IsCritical = s.Kind is SupplyKind.Toner or SupplyKind.Tinta && s.Percent is { } c && c <= criticalPercent
            })
            .ToList();
    }
}
