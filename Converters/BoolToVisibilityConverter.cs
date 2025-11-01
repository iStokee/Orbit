using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts boolean values to Visibility enum values
	/// </summary>
	public class BoolToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not bool boolValue)
				return Visibility.Collapsed;

			// Check for "Inverted" parameter
			bool invert = parameter is string paramStr && paramStr.Equals("Inverted", StringComparison.OrdinalIgnoreCase);

			if (invert)
				boolValue = !boolValue;

			return boolValue ? Visibility.Visible : Visibility.Collapsed;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not Visibility visibility)
				return false;

			bool result = visibility == Visibility.Visible;

			// Check for "Inverted" parameter
			bool invert = parameter is string paramStr && paramStr.Equals("Inverted", StringComparison.OrdinalIgnoreCase);

			return invert ? !result : result;
		}
	}
}
