using Orbit.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Color = System.Windows.Media.Color;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts SessionState to a theme-aware color for visual indicators
	/// </summary>
	public class SessionStateToColorConverter : IValueConverter
	{
		// Fallback colors if theme resources aren't available
		private static readonly SolidColorBrush FallbackGray = new SolidColorBrush(Color.FromRgb(158, 158, 158));
		private static readonly SolidColorBrush FallbackYellow = new SolidColorBrush(Color.FromRgb(255, 193, 7));
		private static readonly SolidColorBrush FallbackBlue = new SolidColorBrush(Color.FromRgb(33, 150, 243));
		private static readonly SolidColorBrush FallbackGreen = new SolidColorBrush(Color.FromRgb(76, 175, 80));
		private static readonly SolidColorBrush FallbackRed = new SolidColorBrush(Color.FromRgb(244, 67, 54));

		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is SessionState state)
			{
				return state switch
				{
					SessionState.Initializing => GetThemeBrush("Orbit.Brushes.Status.Neutral", FallbackGray),
					SessionState.ClientReady => GetThemeBrush("Orbit.Brushes.Status.Warning", FallbackYellow),
					SessionState.Injecting => GetThemeBrush("Orbit.Brushes.Status.Info", FallbackBlue),
					SessionState.Injected => GetThemeBrush("Orbit.Brushes.Status.Success", FallbackGreen),
					SessionState.Failed => GetThemeBrush("Orbit.Brushes.Status.Error", FallbackRed),
					SessionState.ShuttingDown => GetThemeBrush("Orbit.Brushes.Status.Neutral", FallbackGray),
					SessionState.Closed => GetThemeBrush("Orbit.Brushes.Status.Neutral", FallbackGray),
					_ => GetThemeBrush("Orbit.Brushes.Status.Neutral", FallbackGray)
				};
			}

			return GetThemeBrush("Orbit.Brushes.Status.Neutral", FallbackGray);
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotImplementedException();
		}

		/// <summary>
		/// Retrieves a brush from application resources, falling back to a default if not found
		/// </summary>
		private static System.Windows.Media.Brush GetThemeBrush(string resourceKey, System.Windows.Media.Brush fallback)
		{
			try
			{
				if (System.Windows.Application.Current.TryFindResource(resourceKey) is System.Windows.Media.Brush brush)
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
}
