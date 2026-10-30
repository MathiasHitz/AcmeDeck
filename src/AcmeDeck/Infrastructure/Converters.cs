using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using AcmeDeck.Services;

namespace AcmeDeck.Infrastructure;

/// <summary>
/// Maps a CheckLevel to one of the status brushes defined in Styles.xaml.
/// </summary>
public sealed class LevelToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            CheckLevel.Ok => "OkBrush",
            CheckLevel.Warning => "WarningBrush",
            CheckLevel.Error => "ErrorBrush",
            _ => "InfoBrush"
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Light background tint for banners and chips.
/// </summary>
public sealed class LevelToBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value switch
        {
            CheckLevel.Ok => "OkBackgroundBrush",
            CheckLevel.Warning => "WarningBackgroundBrush",
            CheckLevel.Error => "ErrorBackgroundBrush",
            _ => "InfoBackgroundBrush"
        };
        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class LevelToSymbolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CheckLevel.Ok => "✓",
        CheckLevel.Warning => "!",
        CheckLevel.Error => "✕",
        _ => "i"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible when the value is not null, not an empty string and not an empty collection.
/// ConverterParameter "invert" reverses the result.
/// </summary>
public sealed class HasValueToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasValue = value switch
        {
            null => false,
            string s => !string.IsNullOrWhiteSpace(s),
            bool b => b,
            int i => i != 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true
        };
        if (parameter is "invert")
        {
            hasValue = !hasValue;
        }
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible when the bound integer equals the ConverterParameter (wizard steps).
/// </summary>
public sealed class StepToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString() ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

/// <summary>
/// Two-way binding of an enum value to a RadioButton: ConverterParameter is the enum member name.
/// </summary>
public sealed class EnumToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value?.ToString() == parameter?.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter != null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}
