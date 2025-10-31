using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Windows.Data;

namespace Orbit.Converters
{
	public class EnumDescriptionConverter : IValueConverter
	{
		public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
		{
			if (value is Enum enumValue)
			{
				return GetDescription(enumValue);
			}

			return value?.ToString() ?? string.Empty;
		}

		public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}

		private static string GetDescription(Enum value)
		{
			var field = value.GetType().GetField(value.ToString());
			if (field != null)
			{
				var attribute = field.GetCustomAttribute<DescriptionAttribute>();
				if (attribute != null && !string.IsNullOrWhiteSpace(attribute.Description))
				{
					return attribute.Description;
				}
			}

			return value.ToString();
		}
	}
}
