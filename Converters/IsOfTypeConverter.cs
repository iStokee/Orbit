using System;
using System.Globalization;
using System.Windows.Data;

namespace Orbit.Converters;

/// <summary>
/// Returns true if the value is an instance of the type given by ConverterParameter.
/// ConverterParameter can be a Type (x:Type) or a full type name string.
/// </summary>
public sealed class IsOfTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is null || parameter is null)
            return false;

        var targetTypeParam = parameter as Type ?? Type.GetType(parameter.ToString() ?? string.Empty, throwOnError: false);
        if (targetTypeParam == null)
            return false;

        return targetTypeParam.IsInstanceOfType(value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

