using System;
using System.Globalization;
using System.Windows.Data;

namespace Orbit.Converters
{
	public sealed class EnumToBooleanConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value == null || parameter == null)
			{
				return false;
			}

			if (parameter is Enum enumParameter)
			{
				if (value is int intValue)
				{
					return intValue == System.Convert.ToInt32(enumParameter, CultureInfo.InvariantCulture);
				}

				return Equals(value, enumParameter);
			}

			var targetValue = parameter.ToString();
			if (string.IsNullOrWhiteSpace(targetValue))
			{
				return false;
			}

			return string.Equals(value.ToString(), targetValue, StringComparison.Ordinal);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (parameter == null)
			{
				return System.Windows.Data.Binding.DoNothing;
			}

			if (value is not true)
			{
				return System.Windows.Data.Binding.DoNothing;
			}

			if (parameter is Enum enumParameter)
			{
				return targetType == typeof(int)
					? System.Convert.ToInt32(enumParameter, CultureInfo.InvariantCulture)
					: enumParameter;
			}

			return Enum.Parse(targetType, parameter.ToString() ?? string.Empty);
		}
	}
}
