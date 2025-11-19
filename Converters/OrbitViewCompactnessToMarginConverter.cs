using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Orbit.Models;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts OrbitViewCompactness enum to Thickness for margins
	/// </summary>
	public sealed class OrbitViewCompactnessToMarginConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not int compactness)
				return new Thickness(6); // Default: Moderate

			return (OrbitViewCompactness)compactness switch
			{
				OrbitViewCompactness.Minimal => new Thickness(0),
				OrbitViewCompactness.Moderate => new Thickness(8),
				OrbitViewCompactness.Maximum => new Thickness(16),
				_ => new Thickness(6)
			};
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> System.Windows.Data.Binding.DoNothing;
	}
}
