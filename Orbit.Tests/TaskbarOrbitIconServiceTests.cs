using System.Windows.Media;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class TaskbarOrbitIconServiceTests
{
	[Fact]
	public void CreateMainIcon_ReturnsFrozenDrawingImage()
	{
		var service = new TaskbarOrbitIconService();

		var image = service.CreateMainIcon();

		var drawingImage = Assert.IsType<DrawingImage>(image);
		Assert.True(drawingImage.IsFrozen);
	}

	[Fact]
	public void CreateOverlayIcon_ReturnsFrozenDrawingImage()
	{
		var service = new TaskbarOrbitIconService();

		var image = service.CreateOverlayIcon(Colors.DeepSkyBlue, Colors.White);

		var drawingImage = Assert.IsType<DrawingImage>(image);
		Assert.True(drawingImage.IsFrozen);
	}
}
