using System;
using System.Globalization;
using System.Windows.Data;

namespace FalconBMS.Launcher.Converters;

/// <summary>
/// WPF value converter used to compare an enum-bound value to a target value in XAML bindings. 
/// <summary>

public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return false;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}