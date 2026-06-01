using System.Collections.Generic;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

public sealed class ShellPresentationPolicyService
{
	public const string OrbitViewToolKey = "OrbitView";

	public bool CanMoveTabToOrbit(object? target, IEnumerable<object?> orbitItems)
	{
		if (target is SessionModel)
		{
			return true;
		}

		if (target is ToolTabItem tool)
		{
			if (string.Equals(tool.Key, OrbitViewToolKey, System.StringComparison.Ordinal))
			{
				return false;
			}

			return !orbitItems.Any(item => ReferenceEquals(item, tool));
		}

		return false;
	}

	public bool CanMoveSessionToIndividualTabs(object? target, IEnumerable<object?> tabs)
	{
		return target is SessionModel session && !tabs.Any(item => ReferenceEquals(item, session));
	}

	public ToolTabItem? FindToolTab(IEnumerable<object?> tabs, string key)
	{
		return tabs.OfType<ToolTabItem>()
			.FirstOrDefault(t => string.Equals(t.Key, key, System.StringComparison.Ordinal));
	}
}
