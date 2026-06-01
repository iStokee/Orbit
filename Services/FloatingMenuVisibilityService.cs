using Orbit.Models;

namespace Orbit.Services;

public sealed class FloatingMenuVisibilityService
{
	public bool ShouldShowForCurrentTab(
		object? selectedTab,
		int tabCount,
		bool showOnHome,
		bool showOnSessionTabs,
		bool showOnToolTabs)
	{
		if (selectedTab == null)
		{
			if (tabCount == 0)
			{
				return showOnHome || showOnSessionTabs || showOnToolTabs;
			}

			return showOnSessionTabs || showOnToolTabs;
		}

		return selectedTab is SessionModel
			? showOnSessionTabs
			: showOnToolTabs;
	}
}
