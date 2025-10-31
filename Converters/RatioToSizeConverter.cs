using System;
using System.Globalization;
using System.Windows.Data;

namespace Orbit.Converters
{
	public class RatioToSizeConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			if (values.Length < 2 || values[0] is not double size || values[1] is not double ratio)
			{
				return 0d;
			}

			if (double.IsNaN(size) || double.IsInfinity(size))
			{
				return 0d;
			}

			var clampedRatio = Math.Clamp(ratio, 0d, 1d);
			var computed = size * clampedRatio;
			return double.IsNaN(computed) || double.IsInfinity(computed) ? 0d : Math.Max(0d, computed);
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
