using System.Windows;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellWindowPlacementServiceTests
{
	private static readonly Rect VirtualScreen = new(0, 0, 1920, 1080);

	[Fact]
	public void BuildRestorePlan_PrimaryAppliesSavedSizePositionAndMaximizedState()
	{
		var service = new ShellWindowPlacementService();
		var saved = new ShellWindowPlacementSnapshot(1280, 720, 120, 80, Maximized: true);

		var plan = service.BuildRestorePlan(saved, sourceBounds: null, isPrimaryShellWindow: true, VirtualScreen);

		Assert.Equal(1280, plan.Width);
		Assert.Equal(720, plan.Height);
		Assert.Equal(120, plan.Left);
		Assert.Equal(80, plan.Top);
		Assert.True(plan.Maximize);
	}

	[Fact]
	public void BuildRestorePlan_TearOffUsesSourceSizeButIgnoresSavedPositionAndMaximizedState()
	{
		var service = new ShellWindowPlacementService();
		var saved = new ShellWindowPlacementSnapshot(1280, 720, 120, 80, Maximized: true);
		var source = new Rect(20, 30, 1440, 900);

		var plan = service.BuildRestorePlan(saved, source, isPrimaryShellWindow: false, VirtualScreen);

		Assert.Equal(1440, plan.Width);
		Assert.Equal(900, plan.Height);
		Assert.Null(plan.Left);
		Assert.Null(plan.Top);
		Assert.False(plan.Maximize);
	}

	[Fact]
	public void BuildRestorePlan_DoesNotApplyOffscreenSavedPosition()
	{
		var service = new ShellWindowPlacementService();
		var saved = new ShellWindowPlacementSnapshot(1280, 720, 3000, 2000, Maximized: false);

		var plan = service.BuildRestorePlan(saved, sourceBounds: null, isPrimaryShellWindow: true, VirtualScreen);

		Assert.Equal(1280, plan.Width);
		Assert.Equal(720, plan.Height);
		Assert.Null(plan.Left);
		Assert.Null(plan.Top);
	}

	[Fact]
	public void BuildRestorePlan_IgnoresInvalidSmallDimensions()
	{
		var service = new ShellWindowPlacementService();
		var saved = new ShellWindowPlacementSnapshot(320, 240, -1, -1, Maximized: false);
		var source = new Rect(0, 0, 500, 400);

		var plan = service.BuildRestorePlan(saved, source, isPrimaryShellWindow: true, VirtualScreen);

		Assert.Null(plan.Width);
		Assert.Null(plan.Height);
		Assert.Null(plan.Left);
		Assert.Null(plan.Top);
	}
}
