using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Application = System.Windows.Application;
using Binding = System.Windows.Data.Binding;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts a boolean to an accent brush or gray brush.
	/// True = Accent brush, False = Gray brush
	/// </summary>
	public class BooleanToAccentBrushConverter : IValueConverter
	{
		public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (value is bool boolValue)
			{
				if (boolValue)
				{
					// Return accent brush
					return Application.Current?.TryFindResource("MahApps.Brushes.Accent") as SolidColorBrush
						?? new SolidColorBrush(Colors.Blue);
				}
				else
				{
					// Return gray brush
					return Application.Current?.TryFindResource("MahApps.Brushes.Gray6") as SolidColorBrush
						?? new SolidColorBrush(Colors.Gray);
				}
			}

			return Binding.DoNothing;
		}

		public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			return Binding.DoNothing;
		}
	}
}
