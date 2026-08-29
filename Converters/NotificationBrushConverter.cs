using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace BootManager.Converters;

/// <summary>
/// Turns the "is this an error" flag of a notification into the background colour of its banner.
/// </summary>
/// <remarks>
/// XAML cannot express a conditional value directly, so this small adapter bridges the boolean in the
/// view model and the brush the UI needs. <see cref="Instance"/> exists so the XAML can reference the
/// converter with <c>{x:Static ...}</c> instead of declaring it as a resource; the class holds no
/// state, so a single shared instance is safe.
/// </remarks>
public sealed class NotificationBrushConverter : IValueConverter
{
    /// <summary>The shared instance referenced from XAML.</summary>
    public static readonly NotificationBrushConverter Instance = new();

    /// <summary>Returns red for an error and blue for an informational message.</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.IndianRed : Brushes.SteelBlue;

    /// <summary>Not supported: the binding is one-way, a colour is never translated back into a flag.</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
