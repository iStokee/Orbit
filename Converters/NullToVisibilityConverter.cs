using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts null values to Visibility.Collapsed, non-null values to Visibility.Visible
	/// </summary>
	public class NullToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			// If value is a string, also check if it's empty
			if (value is string str)
			{
				return string.IsNullOrWhiteSpace(str) ? Visibility.Collapsed : Visibility.Visible;
			}

			// For other types, check for null
			return value == null ? Visibility.Collapsed : Visibility.Visible;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
