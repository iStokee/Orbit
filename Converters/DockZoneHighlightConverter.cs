using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Orbit.Models;

namespace Orbit.Converters
{
	public class DockZoneHighlightConverter : IMultiValueConverter
	{
		public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
		{
			if (values.Length < 4)
			{
				return Visibility.Collapsed;
			}

			var candidate = values[0];
			var zone = values[1];
			var showAllOnClip = values[2] is bool showOnClip && showOnClip;
			var isClipping = values[3] is bool clipping && clipping;
			var showAllOnDrag = values.Length > 4 && values[4] is bool showOnDrag && showOnDrag;
			var isDragging = values.Length > 5 && values[5] is bool dragging && dragging;

			if ((showAllOnClip && isClipping) || (showAllOnDrag && isDragging))
			{
				return Visibility.Visible;
			}

			if (candidate is FloatingMenuDockRegion current && zone is FloatingMenuDockRegion target)
			{
				return current == target ? Visibility.Visible : Visibility.Collapsed;
			}

			if (candidate != null && candidate.Equals(zone))
			{
				return Visibility.Visible;
			}

			return Visibility.Collapsed;
		}

		public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
		{
			throw new NotSupportedException();
		}
	}
}
