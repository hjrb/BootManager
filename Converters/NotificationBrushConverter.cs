using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace BootManager.Converters;

/// <summary>Converts IsNotificationError to a red (error) or blue (info) background brush.</summary>
public sealed class NotificationBrushConverter : IValueConverter
{
    public static readonly NotificationBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Brushes.IndianRed : Brushes.SteelBlue;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
