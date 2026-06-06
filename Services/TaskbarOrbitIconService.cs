using System;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace Orbit.Services;

public sealed class TaskbarOrbitIconService
{
	private static readonly Geometry OrbitBodyGeometry = CreateOrbitGeometry("M256.25,20.656c-32.78,0-64.03,6.79-92.438,19-8.182-10.618-20.994-17.468-35.437-17.468-24.716,0-44.78,20.033-44.78,44.75,0,8.356,2.324,16.18,6.31,22.874-42.638,42.655-69.093,101.49-69.093,166.282,0,129.617,105.823,234.72,235.438,234.72,129.615-.002,234.72-105.103,234.72-234.72,0-129.618-105.105-235.438-234.72-235.438Zm0,19.313c119.515,0,216.094,96.607,216.094,216.124s-96.58,216.094-216.094,216.094c-119.515,0-216.813-96.577-216.813-216.094,0-59.568,24.176-113.438,63.22-152.5,7.273,5.113,16.15,8.094,25.718,8.094,24.716,0,44.75-20.034,44.75-44.75,0-3.453-.385-6.804-1.125-10.032C197.91,46,226.396,39.97,256.25,39.97Zm-.125,51.81c-91.3,0-165.875,74.575-165.875,165.876,0,91.3,74.576,165.406,165.875,165.406,35.12,0,67.708-10.965,94.5-29.656,7.13,4.23,15.45,6.656,24.344,6.656,26.396,0,47.81-21.384,47.81-47.78,0-12.763-5.005-24.366-13.155-32.938,7.677-19.067,11.906-39.884,11.906-61.688,0-91.3-74.106-165.875-165.405-165.875Zm0,19.126c81.2,0,146.78,65.55,146.78,146.75,0,17.833-3.172,34.924-8.967,50.72-5.81-2.513-12.237-3.907-18.97-3.907-26.396,0-47.78,21.414-47.78,47.81,0,10.59,3.454,20.362,9.28,28.283-23.065,15.084-50.66,23.843-80.343,23.843-81.2,0-147.22-65.55-147.22-146.75s66.02-146.75,147.22-146.75Zm-1.063,19.625c-7.462,31.99-21.767,62.112-42.906,83.25-21.14,21.14-48.73,32.913-80.72,40.376,31.99,7.462,62.112,21.736,83.25,42.875,21.14,21.14,32.914,48.764,40.376,80.75,7.463-31.986,19.204-59.61,40.344-80.75,21.14-21.138,51.262-35.412,83.25-42.874-32.236-7.428-59.455-19.11-80.72-40.375-21.262-21.263-35.446-51.013-42.873-83.25Zm.094,86.564c20.498,0,37.125,16.627,37.125,37.125,0,20.496-16.626,37.124-37.124,37.124-20.497,0-37.125-16.628-37.125-37.125,0-20.5,16.63-37.126,37.126-37.126Z");
	private static readonly Geometry OrbitRingGeometry = CreateOrbitGeometry("M256.219,59.282c-109.882,0-198.813,88.931-198.813,198.813s88.931,198.313,198.813,198.313c28.527,0,55.727-6.064,80.22-17.312-5.983-7.862-9.502-17.646-9.502-28.375,0-26.396,21.384-47.78,47.78-47.78,6.84,0,13.355,1.41,19.214,3.964,5.316-15.913,8.204-32.895,8.204-50.47,0-92.548-75.148-167.653-167.656-167.653Z");

	public ImageSource CreateMainIcon()
		=> CreateIconImage(Colors.White, Colors.White, includeRing: false);

	public ImageSource CreateOverlayIcon(MediaColor accent, MediaColor foreground)
		=> CreateIconImage(accent, foreground, includeRing: true);

	internal static MediaColor AdjustColorBrightness(MediaColor color, double correctionFactor)
	{
		double red = color.R;
		double green = color.G;
		double blue = color.B;

		if (correctionFactor < 0)
		{
			correctionFactor = 1 + correctionFactor;
			red *= correctionFactor;
			green *= correctionFactor;
			blue *= correctionFactor;
		}
		else
		{
			red = (255 - red) * correctionFactor + red;
			green = (255 - green) * correctionFactor + green;
			blue = (255 - blue) * correctionFactor + blue;
		}

		return MediaColor.FromArgb(
			color.A,
			(byte)Math.Clamp(red, 0, 255),
			(byte)Math.Clamp(green, 0, 255),
			(byte)Math.Clamp(blue, 0, 255));
	}

	private static ImageSource CreateIconImage(MediaColor accent, MediaColor foreground, bool includeRing)
	{
		const double targetSize = 56d;

		var fillColor = MediaColor.FromArgb(235, accent.R, accent.G, accent.B);
		var fillBrush = new SolidColorBrush(fillColor);
		fillBrush.Freeze();

		var bodyDrawing = new GeometryDrawing(fillBrush, null, OrbitBodyGeometry);

		var group = new DrawingGroup();
		group.Children.Add(bodyDrawing);

		if (includeRing)
		{
			var ringColor = AdjustColorBrightness(accent, 0.25);
			var ringBrush = new SolidColorBrush(MediaColor.FromArgb(245, ringColor.R, ringColor.G, ringColor.B));
			ringBrush.Freeze();

			var ringPen = new MediaPen(ringBrush, 26)
			{
				StartLineCap = PenLineCap.Round,
				EndLineCap = PenLineCap.Round,
				LineJoin = PenLineJoin.Round
			};
			ringPen.Freeze();

			var ringDrawing = new GeometryDrawing(null, ringPen, OrbitRingGeometry);
			group.Children.Add(ringDrawing);
		}

		var highlightColor = MediaColor.FromArgb(255, foreground.R, foreground.G, foreground.B);
		var highlightBrush = new SolidColorBrush(highlightColor);
		highlightBrush.Freeze();
		var highlightGeometry = new EllipseGeometry(new WindowsPoint(357, 190), 24, 24);
		var highlightDrawing = new GeometryDrawing(highlightBrush, null, highlightGeometry);
		group.Children.Add(highlightDrawing);

		var bounds = OrbitBodyGeometry.Bounds;
		var scale = targetSize / Math.Max(bounds.Width, bounds.Height);
		var translateX = -bounds.X * scale;
		var translateY = -bounds.Y * scale;
		var marginX = (targetSize - bounds.Width * scale) / 2d;
		var marginY = (targetSize - bounds.Height * scale) / 2d;

		var transform = new MatrixTransform(scale, 0, 0, scale, translateX + marginX, translateY + marginY);
		transform.Freeze();
		group.Transform = transform;
		group.Freeze();

		var drawingImage = new DrawingImage(group);
		drawingImage.Freeze();
		return drawingImage;
	}

	private static Geometry CreateOrbitGeometry(string data)
	{
		var geometry = Geometry.Parse(data);
		geometry.Freeze();
		return geometry;
	}
}
