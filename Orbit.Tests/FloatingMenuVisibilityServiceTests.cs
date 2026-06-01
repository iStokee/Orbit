using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class FloatingMenuVisibilityServiceTests
{
	[Theory]
	[InlineData(true, false, false, true)]
	[InlineData(false, true, false, true)]
	[InlineData(false, false, true, true)]
	[InlineData(false, false, false, false)]
	public void ShouldShowForCurrentTab_UsesAnyEnabledModeOnEmptyHome(
		bool showHome,
		bool showSessionTabs,
		bool showToolTabs,
		bool expected)
	{
		var service = new FloatingMenuVisibilityService();

		var shouldShow = service.ShouldShowForCurrentTab(null, 0, showHome, showSessionTabs, showToolTabs);

		Assert.Equal(expected, shouldShow);
	}

	[Theory]
	[InlineData(false, false, false)]
	[InlineData(true, false, true)]
	[InlineData(false, true, true)]
	public void ShouldShowForCurrentTab_IgnoresHomeModeWhenTabsExistAndNoTabSelected(
		bool showSessionTabs,
		bool showToolTabs,
		bool expected)
	{
		var service = new FloatingMenuVisibilityService();

		var shouldShow = service.ShouldShowForCurrentTab(null, 2, true, showSessionTabs, showToolTabs);

		Assert.Equal(expected, shouldShow);
	}

	[Fact]
	public void ShouldShowForCurrentTab_UsesSessionModeForSessionTabs()
	{
		var service = new FloatingMenuVisibilityService();
		var session = new SessionModel { Name = "Session" };

		Assert.True(service.ShouldShowForCurrentTab(session, 1, false, showOnSessionTabs: true, showOnToolTabs: false));
		Assert.False(service.ShouldShowForCurrentTab(session, 1, true, showOnSessionTabs: false, showOnToolTabs: true));
	}

	[Fact]
	public void ShouldShowForCurrentTab_UsesToolModeForNonSessionTabs()
	{
		var service = new FloatingMenuVisibilityService();
		var tool = new object();

		Assert.True(service.ShouldShowForCurrentTab(tool, 1, false, showOnSessionTabs: false, showOnToolTabs: true));
		Assert.False(service.ShouldShowForCurrentTab(tool, 1, true, showOnSessionTabs: true, showOnToolTabs: false));
	}
}
