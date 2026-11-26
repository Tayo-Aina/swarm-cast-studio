using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwarmCastStudio.Converters;

/// <summary>
/// Shows a search watermark only while the box is empty and unfocused.
/// Hiding on focus keeps the caret clear of the watermark overlay.
/// </summary>
public sealed class SearchPlaceholderVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = values.Length > 0 ? values[0] as string : null;
        var focused = values.Length > 1 && values[1] is true;
        return string.IsNullOrEmpty(text) && !focused
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        return Array.Empty<object>();
    }
}
