using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using PrinterManager.App.Models;

namespace PrinterManager.App.Infrastructure;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is Visibility.Visible) ^ Invert;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class EnumDescriptionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is Enum e ? e.GetDescription() : value?.ToString() ?? "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class UtcToLocalConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var format = parameter as string ?? "dd/MM/yyyy HH:mm";
        return value is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc).ToLocalTime().ToString(format) : "—";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ConnectionStatus.Online  => Brush("StatusOnline"),
        ConnectionStatus.Alerta  => Brush("StatusAlert"),
        ConnectionStatus.Erro    => Brush("StatusError"),
        ConnectionStatus.Offline => Brush("StatusOffline"),
        _                        => Brush("StatusUnknown")
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    internal static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);
}

public sealed class SeverityToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AlertSeverity.Critico => StatusToBrushConverter.Brush("StatusError"),
        AlertSeverity.Aviso   => StatusToBrushConverter.Brush("StatusAlert"),
        _                     => StatusToBrushConverter.Brush("StatusUnknown")
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class SupplyColorToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) => ToBrush(value as SupplyColor?);

    public static Brush ToBrush(SupplyColor? color) => color switch
    {
        SupplyColor.Preto   => StatusToBrushConverter.Brush("SupplyBlack"),
        SupplyColor.Ciano   => StatusToBrushConverter.Brush("SupplyCyan"),
        SupplyColor.Magenta => StatusToBrushConverter.Brush("SupplyMagenta"),
        SupplyColor.Amarelo => StatusToBrushConverter.Brush("SupplyYellow"),
        _                   => StatusToBrushConverter.Brush("SupplyOther")
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>[percentual, larguraTotal] → largura preenchida.</summary>
public sealed class PercentToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2 || values[1] is not double width) return 0.0;
        var percent = values[0] switch { double d => d, int i => i, _ => 0.0 };
        return Math.Max(0, Math.Min(width, width * percent / 100.0));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => [];
}
