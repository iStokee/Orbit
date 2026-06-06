using System;
using System.Windows;

namespace Orbit.Services;

public sealed class ShellWindowPlacementService
{
	private const double MinimumWidth = 640d;
	private const double MinimumHeight = 480d;
	private const double MinimumVisibleWidth = 100d;
	private const double MinimumVisibleHeight = 100d;

	public ShellWindowPlacementSnapshot ReadFromSettings()
		=> new(
			Settings.Default.MainWindowWidth,
			Settings.Default.MainWindowHeight,
			Settings.Default.MainWindowLeft,
			Settings.Default.MainWindowTop,
			Settings.Default.MainWindowMaximized);

	public void SaveToSettings(Rect bounds, bool maximized)
	{
		if (!IsFinitePositive(bounds.Width) ||
			!IsFinitePositive(bounds.Height) ||
			!double.IsFinite(bounds.Left) ||
			!double.IsFinite(bounds.Top))
		{
			return;
		}

		Settings.Default.MainWindowWidth = bounds.Width;
		Settings.Default.MainWindowHeight = bounds.Height;
		Settings.Default.MainWindowLeft = bounds.Left;
		Settings.Default.MainWindowTop = bounds.Top;
		Settings.Default.MainWindowMaximized = maximized;
		Settings.Default.Save();
	}

	public ShellWindowPlacementRestorePlan BuildRestorePlan(
		ShellWindowPlacementSnapshot saved,
		Rect? sourceBounds,
		bool isPrimaryShellWindow,
		Rect virtualScreen)
	{
		var width = ChooseDimension(sourceBounds?.Width, saved.Width, MinimumWidth);
		var height = ChooseDimension(sourceBounds?.Height, saved.Height, MinimumHeight);
		double? left = null;
		double? top = null;

		if (isPrimaryShellWindow && HasSavedPosition(saved))
		{
			var candidate = new Rect(saved.Left, saved.Top, width ?? MinimumWidth, height ?? MinimumHeight);
			if (IsVisibleOnVirtualScreen(candidate, virtualScreen))
			{
				left = saved.Left;
				top = saved.Top;
			}
		}

		return new ShellWindowPlacementRestorePlan(
			width,
			height,
			left,
			top,
			isPrimaryShellWindow && saved.Maximized);
	}

	public static bool IsVisibleOnVirtualScreen(Rect rect, Rect virtualScreen)
	{
		if (rect.Width <= 0 || rect.Height <= 0 || virtualScreen.Width <= 0 || virtualScreen.Height <= 0)
		{
			return false;
		}

		var intersection = Rect.Intersect(virtualScreen, rect);
		return !intersection.IsEmpty &&
			intersection.Width >= MinimumVisibleWidth &&
			intersection.Height >= MinimumVisibleHeight;
	}

	private static double? ChooseDimension(double? preferred, double fallback, double minimum)
	{
		if (preferred.HasValue && IsFinitePositive(preferred.Value) && preferred.Value >= minimum)
		{
			return preferred.Value;
		}

		return IsFinitePositive(fallback) && fallback >= minimum
			? fallback
			: null;
	}

	private static bool HasSavedPosition(ShellWindowPlacementSnapshot saved)
		=> !NearlyEquals(saved.Left, -1d) &&
			!NearlyEquals(saved.Top, -1d) &&
			double.IsFinite(saved.Left) &&
			double.IsFinite(saved.Top);

	private static bool IsFinitePositive(double value)
		=> double.IsFinite(value) && value > 0;

	private static bool NearlyEquals(double left, double right)
		=> Math.Abs(left - right) < 0.0001;
}

public sealed record ShellWindowPlacementSnapshot(
	double Width,
	double Height,
	double Left,
	double Top,
	bool Maximized);

public sealed record ShellWindowPlacementRestorePlan(
	double? Width,
	double? Height,
	double? Left,
	double? Top,
	bool Maximize);
