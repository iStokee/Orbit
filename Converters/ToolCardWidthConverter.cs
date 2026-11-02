using System;
using System.Globalization;
using System.Windows.Data;

namespace Orbit.Converters;

public sealed class ToolCardWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double width && double.IsFinite(width) ? Math.Clamp(width - 16, 0, 380) : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
