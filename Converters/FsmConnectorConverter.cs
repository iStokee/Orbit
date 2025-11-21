using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Orbit.Models;
using Point = System.Windows.Point;

namespace Orbit.Converters;

/// <summary>
/// Creates a simple line geometry between two nodes on the canvas.
/// </summary>
public class FsmConnectorConverter : IMultiValueConverter
{
	private const double NodeWidth = 220;
	private const double NodeHeight = 110;

	public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
	{
		if (values.Length < 3)
			return Geometry.Empty;

		if (values[0] is not Guid fromId || values[1] is not Guid toId)
			return Geometry.Empty;

		if (values[2] is not IEnumerable nodesEnumerable)
			return Geometry.Empty;

		var nodes = nodesEnumerable.OfType<FsmNodeModel>().ToList();
		var from = nodes.FirstOrDefault(n => n.Id == fromId);
		var to = nodes.FirstOrDefault(n => n.Id == toId);

		if (from == null || to == null)
			return Geometry.Empty;

		var start = new Point(from.X + NodeWidth / 2, from.Y + NodeHeight / 2);
		var end = new Point(to.X + NodeWidth / 2, to.Y + NodeHeight / 2);

		// Slight curve for readability
		var controlOffset = (end.X - start.X) / 2;
		var control1 = new Point(start.X + controlOffset, start.Y);
		var control2 = new Point(end.X - controlOffset, end.Y);

		var figure = new PathFigure { StartPoint = start, IsFilled = false, IsClosed = false };
		figure.Segments.Add(new BezierSegment(control1, control2, end, true));

		var geometry = new PathGeometry();
		geometry.Figures.Add(figure);
		return geometry;
	}

	public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
	{
		throw new NotImplementedException();
	}
}
