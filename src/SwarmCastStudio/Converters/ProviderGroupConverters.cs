using System.Globalization;
using System.Windows.Data;

namespace SwarmCastStudio.Converters;

/// <summary>
/// Builds the model provider group key used by the grouped CollectionView.
/// The numeric prefix controls group order; the header converter hides it.
/// </summary>
public sealed class ProviderGroupKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var provider = value as string ?? string.Empty;
        var trimmed = provider.Trim();
        if (trimmed.Length == 0)
        {
            return "3|unknown";
        }

        var rank = trimmed.ToLowerInvariant() switch
        {
            "opencode-go" => "0",
            "opencode" => "1",
            "unknown" => "3",
            _ => "2"
        };

        return rank + "|" + trimmed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}

/// <summary>
/// Removes a leading provider group sort prefix such as "0|" for display.
/// Values without a prefix are returned unchanged (used by agent category groups too).
/// </summary>
public sealed class GroupDisplayNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value?.ToString() ?? string.Empty;
        var separator = name.IndexOf('|');
        return separator >= 0 && separator < name.Length - 1
            ? name[(separator + 1)..]
            : name;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}
