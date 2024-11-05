using System;
using System.Globalization;
using System.Windows.Data;

namespace Orbit.Classes
{
	public class IntPtrToStringConverter : IValueConverter
	{
		public static IntPtrToStringConverter Instance = new IntPtrToStringConverter();
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is IntPtr ptr)
				return ptr.ToString("X"); // Display in hexadecimal for clarity
			return string.Empty;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			return IntPtr.Zero; // ConvertBack is not needed
		}
	}
}
