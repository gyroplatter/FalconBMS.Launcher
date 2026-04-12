using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace FalconBMS.Launcher.Converters;

/// <summary>
/// WPF multi-value converter that turns a normalized fraction and bar width into a canvas X position.
/// </summary>

public sealed class FractionToCanvasLeftConverter : IMultiValueConverter
{
    // values[0] = fraction (0..1)
    // values[1] = barWidth (pixels)
    // parameter = optional subtract (string/double) (e.g. half stroke thickness)
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2)
            return 0.0;

        if (!TryToDouble(values[0], out double frac)) return 0.0;
        if (!TryToDouble(values[1], out double width)) return 0.0;

        if (double.IsNaN(frac) || double.IsInfinity(frac)) frac = 0.0;
        if (double.IsNaN(width) || double.IsInfinity(width) || width <= 0.0) return 0.0;

        if (frac < 0.0) frac = 0.0;
        if (frac > 1.0) frac = 1.0;

        double subtract = 0.0;
        if (parameter is not null)
        {
            if (parameter is double d) subtract = d;
            else if (double.TryParse(parameter.ToString(), NumberStyles.Any, culture, out var parsed)) subtract = parsed;
        }

        double left = (frac * width) - subtract;

        if (left < 0.0) left = 0.0;
        if (left > width) left = width;

        return left;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();

    private static bool TryToDouble(object v, out double result)
    {
        if (v is double d) { result = d; return true; }
        if (v is float f) { result = f; return true; }
        if (v is int i) { result = i; return true; }
        if (v is long l) { result = l; return true; }
        if (v is string s && double.TryParse(s, out var parsed)) { result = parsed; return true; }

        try
        {
            result = System.Convert.ToDouble(v);
            return true;
        }
        catch
        {
            result = 0.0;
            return false;
        }
    }
}