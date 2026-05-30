using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Binding = System.Windows.Data.Binding;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace Orbit.Converters
{
	public sealed class ContrastForegroundBrushConverter : IValueConverter
	{
		private static readonly SolidColorBrush BlackBrush = CreateFrozenBrush(Colors.Black);
		private static readonly SolidColorBrush WhiteBrush = CreateFrozenBrush(Colors.White);

		public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
		{
			if (!TryResolveColor(value, out var background))
			{
				return Binding.DoNothing;
			}

			var blackContrast = GetContrastRatio(background, Colors.Black);
			var whiteContrast = GetContrastRatio(background, Colors.White);
			return blackContrast >= whiteContrast ? BlackBrush : WhiteBrush;
		}

		public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
			=> Binding.DoNothing;

		private static bool TryResolveColor(object? value, out Color color)
		{
			switch (value)
			{
				case Color direct:
					color = direct;
					return true;
				case SolidColorBrush brush:
					color = brush.Color;
					return true;
				case string hex when !string.IsNullOrWhiteSpace(hex):
					try
					{
						var parsed = ColorConverter.ConvertFromString(hex);
						if (parsed is Color parsedColor)
						{
							color = parsedColor;
							return true;
						}
					}
					catch
					{
						// Fall through to the default return value.
					}

					break;
			}

			color = Colors.Transparent;
			return false;
		}

		private static double GetContrastRatio(Color colorA, Color colorB)
		{
			var luminanceA = GetRelativeLuminance(colorA);
			var luminanceB = GetRelativeLuminance(colorB);
			var lighter = Math.Max(luminanceA, luminanceB);
			var darker = Math.Min(luminanceA, luminanceB);
			return (lighter + 0.05) / (darker + 0.05);
		}

		private static double GetRelativeLuminance(Color color)
		{
			static double ToLinear(byte component)
			{
				var srgb = component / 255.0;
				return srgb <= 0.03928
					? srgb / 12.92
					: Math.Pow((srgb + 0.055) / 1.055, 2.4);
			}

			return (0.2126 * ToLinear(color.R)) +
			       (0.7152 * ToLinear(color.G)) +
			       (0.0722 * ToLinear(color.B));
		}

		private static SolidColorBrush CreateFrozenBrush(Color color)
		{
			var brush = new SolidColorBrush(color);
			brush.Freeze();
			return brush;
		}
	}
}
