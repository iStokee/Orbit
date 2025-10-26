using System;
using System.Globalization;
using System.Windows.Data;

namespace Orbit.Converters
{
	public class SubtractConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is double doubleValue && parameter != null)
			{
				if (double.TryParse(parameter.ToString(), out var subtractValue))
				{
					return doubleValue - subtractValue;
				}
			}

			return value;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
