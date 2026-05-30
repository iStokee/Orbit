using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Orbit.Logging;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace Orbit.Converters;

/// <summary>
/// Maps console log level to a theme-aware brush.
/// Uses DynamicResource lookup to respond to theme changes.
/// </summary>
public sealed class ConsoleLogLevelToBrushConverter : IValueConverter
{
	// Fallback colors if theme resources aren't available
	private static readonly SolidColorBrush FallbackGray = new(Color.FromRgb(158, 158, 158));
	private static readonly SolidColorBrush FallbackWhite = new(Colors.White);
	private static readonly SolidColorBrush FallbackGold = new(Colors.Gold);
	private static readonly SolidColorBrush FallbackOrangeRed = new(Colors.OrangeRed);
	private static readonly SolidColorBrush FallbackRed = new(Colors.Red);

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is not ConsoleLogLevel level)
			return GetThemeBrush("Orbit.Brushes.Text.Primary", FallbackWhite);

		return level switch
		{
			ConsoleLogLevel.Debug => GetThemeBrush("Orbit.Brushes.Status.Neutral", FallbackGray),
			ConsoleLogLevel.Info => GetThemeBrush("Orbit.Brushes.Text.Primary", FallbackWhite),
			ConsoleLogLevel.Warning => GetThemeBrush("Orbit.Brushes.Status.Warning", FallbackGold),
			ConsoleLogLevel.Error => GetThemeBrush("Orbit.Brushes.Status.Error", FallbackOrangeRed),
			ConsoleLogLevel.Critical => GetThemeBrush("Orbit.Brushes.Status.Error", FallbackRed),
			_ => GetThemeBrush("Orbit.Brushes.Text.Primary", FallbackWhite)
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();

	/// <summary>
	/// Retrieves a brush from application resources, falling back to a default if not found
	/// </summary>
	private static Brush GetThemeBrush(string resourceKey, Brush fallback)
	{
		try
		{
			if (System.Windows.Application.Current.TryFindResource(resourceKey) is Brush brush)
			{
				return brush;
			}
		}
		catch
		{
			// Resource lookup failed, use fallback
		}

		return fallback;
	}
}
