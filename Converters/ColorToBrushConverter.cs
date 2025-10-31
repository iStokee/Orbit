using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Orbit.Converters
{
	public class ColorToBrushConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is SolidColorBrush brush)
			{
				return brush;
			}

			if (value is Color color)
			{
				return new SolidColorBrush(color);
			}

			if (value is string hex && !string.IsNullOrWhiteSpace(hex))
			{
				try
				{
					var converted = ColorConverter.ConvertFromString(hex);
					if (converted is Color parsed)
					{
						return new SolidColorBrush(parsed);
					}
				}
				catch
				{
					// Ignore parse failures; fall through to default.
				}
			}

			return Binding.DoNothing;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is SolidColorBrush brush)
			{
				return brush.Color;
			}

			return Binding.DoNothing;
		}
	}
}
