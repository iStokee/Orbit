using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Orbit.Logging;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;

namespace Orbit.Converters;

public sealed class ConsoleLogLevelToBrushConverter : IValueConverter
{
	public Brush DebugBrush { get; set; } = Brushes.Gray;
	public Brush InfoBrush { get; set; } = Brushes.White;
	public Brush WarningBrush { get; set; } = Brushes.Gold;
	public Brush ErrorBrush { get; set; } = Brushes.OrangeRed;
	public Brush CriticalBrush { get; set; } = Brushes.Red;

	public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
	{
		if (value is not ConsoleLogLevel level)
			return InfoBrush;

		return level switch
		{
			ConsoleLogLevel.Debug => DebugBrush,
			ConsoleLogLevel.Info => InfoBrush,
			ConsoleLogLevel.Warning => WarningBrush,
			ConsoleLogLevel.Error => ErrorBrush,
			ConsoleLogLevel.Critical => CriticalBrush,
			_ => InfoBrush
		};
	}

	public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		=> throw new NotSupportedException();
}
