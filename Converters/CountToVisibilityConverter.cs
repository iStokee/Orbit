using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

namespace Orbit.Converters
{
	public sealed class CountToVisibilityConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is int count)
			{
				var showWhenZero = parameter is string param &&
				                   param.Equals("Zero", StringComparison.OrdinalIgnoreCase);

				var isVisible = showWhenZero ? count == 0 : count > 0;
				return isVisible ? Visibility.Visible : Visibility.Collapsed;
			}

			return Visibility.Collapsed;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> Binding.DoNothing;
	}
}
