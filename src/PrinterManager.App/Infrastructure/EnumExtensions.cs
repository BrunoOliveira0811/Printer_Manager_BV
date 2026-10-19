using System.ComponentModel;
using System.Reflection;

namespace PrinterManager.App;

public static class EnumExtensions
{
    public static string GetDescription(this Enum value) =>
        value.GetType().GetField(value.ToString())?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? value.ToString();
}

/// <summary>Item para ComboBox de enums com texto amigável.</summary>
public sealed record EnumOption<T>(T Value, string Text) where T : struct, Enum
{
    public static IReadOnlyList<EnumOption<T>> All { get; } =
        Enum.GetValues<T>().Select(v => new EnumOption<T>(v, v.GetDescription())).ToList();

    public override string ToString() => Text;
}
