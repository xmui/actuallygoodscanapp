using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ScanApp.App.Infrastructure;

/// <summary>true -> Visible, false -> Collapsed (use ConverterParameter "invert" to flip).</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is true;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            b = !b;
        }
        return b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

/// <summary>non-null -> Visible, null -> Collapsed (ConverterParameter "invert" flips).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool visible = value is not null;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
