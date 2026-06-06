using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using Orbit.Models;

namespace Orbit.Services;

public sealed class FloatingMenuGeometryService
{
	private const double HandleRadius = 24d;
	private const double DockEdgeMargin = 24d;

	public FloatingMenuDirection ComputeAutoActiveSide(
		double left,
		double top,
		double viewportWidth,
		double viewportHeight,
		FloatingMenuDirection fallback)
	{
		if (viewportWidth <= 0 || viewportHeight <= 0)
		{
			return fallback;
		}

		var centerX = left + HandleRadius;
		var centerY = top + HandleRadius;

		var leftDistance = Math.Max(0, centerX);
		var rightDistance = Math.Max(0, viewportWidth - centerX);
		var upDistance = Math.Max(0, centerY);
		var downDistance = Math.Max(0, viewportHeight - centerY);

		var best = fallback;
		var bestDistance = double.MaxValue;
		SelectIfCloser(FloatingMenuDirection.Left, leftDistance, ref best, ref bestDistance);
		SelectIfCloser(FloatingMenuDirection.Right, rightDistance, ref best, ref bestDistance);
		SelectIfCloser(FloatingMenuDirection.Up, upDistance, ref best, ref bestDistance);
		SelectIfCloser(FloatingMenuDirection.Down, downDistance, ref best, ref bestDistance);
		return best;
	}

	public PlacementMode ToPlacement(FloatingMenuDirection side) => side switch
	{
		FloatingMenuDirection.Left => PlacementMode.Left,
		FloatingMenuDirection.Right => PlacementMode.Right,
		FloatingMenuDirection.Up => PlacementMode.Top,
		FloatingMenuDirection.Down => PlacementMode.Bottom,
		_ => PlacementMode.Right
	};

	public PlacementMode OppositeToPlacement(FloatingMenuDirection side) => side switch
	{
		FloatingMenuDirection.Left => PlacementMode.Right,
		FloatingMenuDirection.Right => PlacementMode.Left,
		FloatingMenuDirection.Up => PlacementMode.Bottom,
		FloatingMenuDirection.Down => PlacementMode.Top,
		_ => PlacementMode.Right
	};

	public FloatingMenuDirection OppositeOf(FloatingMenuDirection side) => side switch
	{
		FloatingMenuDirection.Left => FloatingMenuDirection.Right,
		FloatingMenuDirection.Right => FloatingMenuDirection.Left,
		FloatingMenuDirection.Up => FloatingMenuDirection.Down,
		FloatingMenuDirection.Down => FloatingMenuDirection.Up,
		_ => FloatingMenuDirection.Right
	};

	public FloatingMenuDirection DetermineDirectionForRegion(FloatingMenuDockRegion region)
	{
		return region switch
		{
			FloatingMenuDockRegion.Left => FloatingMenuDirection.Right,
			FloatingMenuDockRegion.Right => FloatingMenuDirection.Left,
			FloatingMenuDockRegion.Top => FloatingMenuDirection.Down,
			FloatingMenuDockRegion.Bottom => FloatingMenuDirection.Up,
			FloatingMenuDockRegion.TopLeft => FloatingMenuDirection.Right,
			FloatingMenuDockRegion.TopRight => FloatingMenuDirection.Left,
			FloatingMenuDockRegion.BottomLeft => FloatingMenuDirection.Right,
			FloatingMenuDockRegion.BottomRight => FloatingMenuDirection.Left,
			_ => FloatingMenuDirection.Right
		};
	}

	public (double Left, double Top) ComputeDockPosition(
		FloatingMenuDockRegion region,
		double currentLeft,
		double currentTop,
		double handleWidth,
		double handleHeight,
		double viewportWidth,
		double viewportHeight)
	{
		if (region == FloatingMenuDockRegion.None)
		{
			return (currentLeft, currentTop);
		}

		var usableWidth = Math.Max(0, viewportWidth - handleWidth);
		var usableHeight = Math.Max(0, viewportHeight - handleHeight);
		var centerLeft = usableWidth / 2;
		var centerTop = usableHeight / 2;
		var leftEdge = Math.Max(0, DockEdgeMargin);
		var topEdge = Math.Max(0, DockEdgeMargin);
		var rightEdge = Math.Max(0, viewportWidth - handleWidth - DockEdgeMargin);
		var bottomEdge = Math.Max(0, viewportHeight - handleHeight - DockEdgeMargin);

		var newLeft = currentLeft;
		var newTop = currentTop;

		switch (region)
		{
			case FloatingMenuDockRegion.Left:
				newLeft = leftEdge;
				newTop = centerTop;
				break;
			case FloatingMenuDockRegion.Right:
				newLeft = rightEdge;
				newTop = centerTop;
				break;
			case FloatingMenuDockRegion.Top:
				newLeft = centerLeft;
				newTop = topEdge;
				break;
			case FloatingMenuDockRegion.Bottom:
				newLeft = centerLeft;
				newTop = bottomEdge;
				break;
			case FloatingMenuDockRegion.TopLeft:
				newLeft = leftEdge;
				newTop = topEdge;
				break;
			case FloatingMenuDockRegion.TopRight:
				newLeft = rightEdge;
				newTop = topEdge;
				break;
			case FloatingMenuDockRegion.BottomLeft:
				newLeft = leftEdge;
				newTop = bottomEdge;
				break;
			case FloatingMenuDockRegion.BottomRight:
				newLeft = rightEdge;
				newTop = bottomEdge;
				break;
			case FloatingMenuDockRegion.Center:
				newLeft = centerLeft;
				newTop = centerTop;
				break;
		}

		return (Math.Clamp(newLeft, 0, usableWidth), Math.Clamp(newTop, 0, usableHeight));
	}

	public CornerRadius BuildDockCornerRadius(double cornerThreshold, double cornerHeight, double roundness)
	{
		var cornerWidth = Math.Clamp(cornerThreshold, 60d, 250d);
		var normalizedCornerHeight = Math.Clamp(cornerHeight, 60d, 250d);
		var extent = Math.Min(cornerWidth, normalizedCornerHeight);
		var normalizedRoundness = Math.Clamp(roundness, 0d, 1d);
		var radius = Math.Max(0d, extent * normalizedRoundness);
		return new CornerRadius(radius);
	}

	public (FloatingMenuDockRegion Region, bool Clipped) DetectSnapZone(
		double left,
		double top,
		double handleWidth,
		double handleHeight,
		double hostWidth,
		double hostHeight,
		double snapThreshold,
		double cornerSize,
		double cornerHeight,
		double edgeCoverage)
	{
		snapThreshold = Math.Clamp(snapThreshold, 40d, 200d);
		cornerSize = Math.Clamp(cornerSize, 60d, 250d);
		cornerHeight = Math.Clamp(cornerHeight, 60d, 250d);
		edgeCoverage = Math.Clamp(edgeCoverage, 0.05d, 0.95d);

		hostWidth = Math.Max(hostWidth, handleWidth);
		hostHeight = Math.Max(hostHeight, handleHeight);

		var handleRect = new Rect(left, top, Math.Max(0d, handleWidth), Math.Max(0d, handleHeight));

		double ClampDimension(double value, double max) => Math.Max(0d, Math.Min(value, max));

		var leftThickness = ClampDimension(snapThreshold, hostWidth);
		var rightThickness = leftThickness;
		var topThickness = ClampDimension(snapThreshold, hostHeight);
		var bottomThickness = topThickness;

		var verticalCoverage = ClampDimension(hostHeight * edgeCoverage, hostHeight);
		var verticalStart = (hostHeight - verticalCoverage) / 2d;
		var horizontalCoverage = ClampDimension(hostWidth * edgeCoverage, hostWidth);
		var horizontalStart = (hostWidth - horizontalCoverage) / 2d;

		var cornerWidth = ClampDimension(cornerSize, hostWidth);
		var cornerHeightClamped = ClampDimension(cornerHeight, hostHeight);

		var leftZone = new Rect(0d, verticalStart, leftThickness, verticalCoverage);
		var rightZone = new Rect(Math.Max(0d, hostWidth - rightThickness), verticalStart, rightThickness, verticalCoverage);
		var topZone = new Rect(horizontalStart, 0d, horizontalCoverage, topThickness);
		var bottomZone = new Rect(horizontalStart, Math.Max(0d, hostHeight - bottomThickness), horizontalCoverage, bottomThickness);

		var topLeftZone = new Rect(0d, 0d, cornerWidth, cornerHeightClamped);
		var topRightZone = new Rect(Math.Max(0d, hostWidth - cornerWidth), 0d, cornerWidth, cornerHeightClamped);
		var bottomLeftZone = new Rect(0d, Math.Max(0d, hostHeight - cornerHeightClamped), cornerWidth, cornerHeightClamped);
		var bottomRightZone = new Rect(Math.Max(0d, hostWidth - cornerWidth), Math.Max(0d, hostHeight - cornerHeightClamped), cornerWidth, cornerHeightClamped);

		bool Intersects(Rect zone) => zone.Width > 0d && zone.Height > 0d && zone.IntersectsWith(handleRect);

		var topLeftActive = Intersects(topLeftZone);
		var topRightActive = Intersects(topRightZone);
		var bottomLeftActive = Intersects(bottomLeftZone);
		var bottomRightActive = Intersects(bottomRightZone);

		var leftActive = Intersects(leftZone);
		var rightActive = Intersects(rightZone);
		var topActive = Intersects(topZone);
		var bottomActive = Intersects(bottomZone);

		var anyActive = topLeftActive ||
			topRightActive ||
			bottomLeftActive ||
			bottomRightActive ||
			leftActive ||
			rightActive ||
			topActive ||
			bottomActive;

		if (topLeftActive)
			return (FloatingMenuDockRegion.TopLeft, anyActive);
		if (topRightActive)
			return (FloatingMenuDockRegion.TopRight, anyActive);
		if (bottomLeftActive)
			return (FloatingMenuDockRegion.BottomLeft, anyActive);
		if (bottomRightActive)
			return (FloatingMenuDockRegion.BottomRight, anyActive);

		if (leftActive)
			return (FloatingMenuDockRegion.Left, anyActive);
		if (rightActive)
			return (FloatingMenuDockRegion.Right, anyActive);
		if (topActive)
			return (FloatingMenuDockRegion.Top, anyActive);
		if (bottomActive)
			return (FloatingMenuDockRegion.Bottom, anyActive);

		return (FloatingMenuDockRegion.None, anyActive);
	}

	private static void SelectIfCloser(
		FloatingMenuDirection side,
		double distance,
		ref FloatingMenuDirection best,
		ref double bestDistance)
	{
		if (distance < bestDistance)
		{
			bestDistance = distance;
			best = side;
		}
	}
}
