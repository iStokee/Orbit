using System.Windows.Controls.Primitives;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class FloatingMenuGeometryServiceTests
{
	[Theory]
	[InlineData(0, 200, FloatingMenuDirection.Left)]
	[InlineData(960, 200, FloatingMenuDirection.Right)]
	[InlineData(300, 0, FloatingMenuDirection.Up)]
	[InlineData(300, 760, FloatingMenuDirection.Down)]
	public void ComputeAutoActiveSide_ChoosesNearestViewportEdge(double left, double top, FloatingMenuDirection expected)
	{
		var service = new FloatingMenuGeometryService();

		var side = service.ComputeAutoActiveSide(left, top, 1000, 800, FloatingMenuDirection.Right);

		Assert.Equal(expected, side);
	}

	[Fact]
	public void ComputeAutoActiveSide_ReturnsFallbackForInvalidViewport()
	{
		var service = new FloatingMenuGeometryService();

		var side = service.ComputeAutoActiveSide(0, 0, 0, 800, FloatingMenuDirection.Down);

		Assert.Equal(FloatingMenuDirection.Down, side);
	}

	[Theory]
	[InlineData(FloatingMenuDirection.Left, PlacementMode.Left, PlacementMode.Right, FloatingMenuDirection.Right)]
	[InlineData(FloatingMenuDirection.Right, PlacementMode.Right, PlacementMode.Left, FloatingMenuDirection.Left)]
	[InlineData(FloatingMenuDirection.Up, PlacementMode.Top, PlacementMode.Bottom, FloatingMenuDirection.Down)]
	[InlineData(FloatingMenuDirection.Down, PlacementMode.Bottom, PlacementMode.Top, FloatingMenuDirection.Up)]
	public void PlacementMappings_AreConsistent(
		FloatingMenuDirection side,
		PlacementMode expectedPlacement,
		PlacementMode expectedOppositePlacement,
		FloatingMenuDirection expectedOppositeSide)
	{
		var service = new FloatingMenuGeometryService();

		Assert.Equal(expectedPlacement, service.ToPlacement(side));
		Assert.Equal(expectedOppositePlacement, service.OppositeToPlacement(side));
		Assert.Equal(expectedOppositeSide, service.OppositeOf(side));
	}

	[Theory]
	[InlineData(100, 80, 0.5, 40)]
	[InlineData(40, 40, 1, 60)]
	[InlineData(300, 300, 1, 250)]
	[InlineData(120, 120, -1, 0)]
	public void BuildDockCornerRadius_ClampsInputs(double width, double height, double roundness, double expectedRadius)
	{
		var service = new FloatingMenuGeometryService();

		var radius = service.BuildDockCornerRadius(width, height, roundness);

		Assert.Equal(expectedRadius, radius.TopLeft);
		Assert.Equal(expectedRadius, radius.TopRight);
		Assert.Equal(expectedRadius, radius.BottomLeft);
		Assert.Equal(expectedRadius, radius.BottomRight);
	}

	[Theory]
	[InlineData(FloatingMenuDockRegion.Left, 24, 374)]
	[InlineData(FloatingMenuDockRegion.Right, 924, 374)]
	[InlineData(FloatingMenuDockRegion.Top, 474, 24)]
	[InlineData(FloatingMenuDockRegion.Bottom, 474, 724)]
	[InlineData(FloatingMenuDockRegion.Center, 474, 374)]
	public void ComputeDockPosition_MapsRegionsToViewportPositions(FloatingMenuDockRegion region, double expectedLeft, double expectedTop)
	{
		var service = new FloatingMenuGeometryService();

		var position = service.ComputeDockPosition(region, 10, 20, 52, 52, 1000, 800);

		Assert.Equal(expectedLeft, position.Left);
		Assert.Equal(expectedTop, position.Top);
	}

	[Theory]
	[InlineData(FloatingMenuDockRegion.Left, FloatingMenuDirection.Right)]
	[InlineData(FloatingMenuDockRegion.Right, FloatingMenuDirection.Left)]
	[InlineData(FloatingMenuDockRegion.Top, FloatingMenuDirection.Down)]
	[InlineData(FloatingMenuDockRegion.Bottom, FloatingMenuDirection.Up)]
	[InlineData(FloatingMenuDockRegion.None, FloatingMenuDirection.Right)]
	public void DetermineDirectionForRegion_ChoosesExpansionTowardViewport(FloatingMenuDockRegion region, FloatingMenuDirection expected)
	{
		var service = new FloatingMenuGeometryService();

		Assert.Equal(expected, service.DetermineDirectionForRegion(region));
	}

	[Theory]
	[InlineData(4, 4, FloatingMenuDockRegion.TopLeft)]
	[InlineData(940, 4, FloatingMenuDockRegion.TopRight)]
	[InlineData(4, 740, FloatingMenuDockRegion.BottomLeft)]
	[InlineData(940, 740, FloatingMenuDockRegion.BottomRight)]
	[InlineData(4, 360, FloatingMenuDockRegion.Left)]
	[InlineData(940, 360, FloatingMenuDockRegion.Right)]
	[InlineData(480, 4, FloatingMenuDockRegion.Top)]
	[InlineData(480, 740, FloatingMenuDockRegion.Bottom)]
	public void DetectSnapZone_ReturnsNearestActiveRegion(double left, double top, FloatingMenuDockRegion expected)
	{
		var service = new FloatingMenuGeometryService();

		var detection = service.DetectSnapZone(
			left,
			top,
			handleWidth: 52,
			handleHeight: 52,
			hostWidth: 1000,
			hostHeight: 800,
			snapThreshold: 80,
			cornerSize: 100,
			cornerHeight: 100,
			edgeCoverage: 0.5);

		Assert.Equal(expected, detection.Region);
		Assert.True(detection.Clipped);
	}

	[Fact]
	public void DetectSnapZone_ReturnsNoneAwayFromZones()
	{
		var service = new FloatingMenuGeometryService();

		var detection = service.DetectSnapZone(
			left: 480,
			top: 360,
			handleWidth: 52,
			handleHeight: 52,
			hostWidth: 1000,
			hostHeight: 800,
			snapThreshold: 80,
			cornerSize: 100,
			cornerHeight: 100,
			edgeCoverage: 0.5);

		Assert.Equal(FloatingMenuDockRegion.None, detection.Region);
		Assert.False(detection.Clipped);
	}
}
