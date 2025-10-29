using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts MahApps color scheme names to actual colors for visual display
	/// </summary>
	public class ColorSchemeToColorConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is string colorScheme)
			{
				return colorScheme switch
				{
					// MahApps standard color schemes
					"Red" => Color.FromRgb(229, 20, 0),
					"Green" => Color.FromRgb(96, 169, 23),
					"Blue" => Color.FromRgb(0, 122, 204),
					"Purple" => Color.FromRgb(170, 0, 255),
					"Orange" => Color.FromRgb(250, 104, 0),
					"Lime" => Color.FromRgb(164, 196, 0),
					"Emerald" => Color.FromRgb(0, 138, 0),
					"Teal" => Color.FromRgb(0, 171, 169),
					"Cyan" => Color.FromRgb(27, 161, 226),
					"Cobalt" => Color.FromRgb(0, 80, 239),
					"Indigo" => Color.FromRgb(106, 0, 255),
					"Violet" => Color.FromRgb(170, 0, 255),
					"Pink" => Color.FromRgb(244, 114, 208),
					"Magenta" => Color.FromRgb(216, 0, 115),
					"Crimson" => Color.FromRgb(162, 0, 37),
					"Amber" => Color.FromRgb(240, 163, 10),
					"Yellow" => Color.FromRgb(227, 200, 0),
					"Brown" => Color.FromRgb(130, 90, 44),
					"Olive" => Color.FromRgb(109, 135, 100),
					"Steel" => Color.FromRgb(100, 118, 135),
					"Mauve" => Color.FromRgb(118, 96, 138),
					"Taupe" => Color.FromRgb(135, 121, 78),
					"Sienna" => Color.FromRgb(122, 59, 40),

					// Fallback
					_ => Color.FromRgb(100, 118, 135) // Steel as default
				};
			}

			return Color.FromRgb(100, 118, 135);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}
	}
}
