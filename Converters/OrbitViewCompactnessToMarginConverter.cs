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
				return new Thickness(5); // Default: Moderate

			return (OrbitViewCompactness)compactness switch
			{
				OrbitViewCompactness.Minimal => new Thickness(1),
				OrbitViewCompactness.Moderate => new Thickness(5),
				OrbitViewCompactness.Maximum => new Thickness(10),
				_ => new Thickness(5)
			};
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> System.Windows.Data.Binding.DoNothing;
	}
}
