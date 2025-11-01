using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Orbit.Models;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts OrbitViewCompactness enum to Thickness for grid cell margins (smaller values)
	/// </summary>
	public sealed class OrbitViewCompactnessToCellMarginConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not int compactness)
				return new Thickness(4); // Default: Moderate

			return (OrbitViewCompactness)compactness switch
			{
				OrbitViewCompactness.Minimal => new Thickness(1),
				OrbitViewCompactness.Moderate => new Thickness(4),
				OrbitViewCompactness.Maximum => new Thickness(8),
				_ => new Thickness(4)
			};
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> System.Windows.Data.Binding.DoNothing;
	}
}
