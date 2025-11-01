using System;
using System.Globalization;
using System.Windows.Data;
using Orbit.Models;

namespace Orbit.Converters
{
	/// <summary>
	/// Converts OrbitViewTabHeaderSize enum to MinHeight value
	/// </summary>
	public sealed class OrbitViewTabHeaderSizeToHeightConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is not int headerSize)
				return 32.0; // Default: Standard

			return (OrbitViewTabHeaderSize)headerSize switch
			{
				OrbitViewTabHeaderSize.Compact => 24.0,
				OrbitViewTabHeaderSize.Standard => 32.0,
				OrbitViewTabHeaderSize.Comfortable => 40.0,
				_ => 32.0
			};
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
			=> System.Windows.Data.Binding.DoNothing;
	}
}
