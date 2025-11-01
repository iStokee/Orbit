using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Orbit.Models;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts OrbitViewBorderThickness enum to Thickness
	/// </summary>
	public sealed class OrbitViewBorderThicknessConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not int borderThickness)
				return new Thickness(2); // Default: Standard

			return (OrbitViewBorderThickness)borderThickness switch
			{
				OrbitViewBorderThickness.None => new Thickness(0),
				OrbitViewBorderThickness.Minimal => new Thickness(1),
				OrbitViewBorderThickness.Standard => new Thickness(2),
				_ => new Thickness(2)
			};
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> System.Windows.Data.Binding.DoNothing;
	}
}
