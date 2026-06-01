using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Orbit.Services;

public sealed class ShellTabSelectionService
{
	public ShellTabSelectionResult ResolveAfterCollectionChanged(
		IEnumerable<object?> tabs,
		object? currentSelectedTab,
		NotifyCollectionChangedAction action,
		IEnumerable<object?>? oldItems,
		IEnumerable<object?>? newItems)
	{
		var snapshot = tabs.ToList();
		var oldSnapshot = oldItems?.ToList();

		if (action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
		{
			if (snapshot.Count == 0)
			{
				return new ShellTabSelectionResult(null, ClearSelectedSession: true);
			}

			if (currentSelectedTab == null || ContainsReference(oldSnapshot, currentSelectedTab))
			{
				return new ShellTabSelectionResult(snapshot.FirstOrDefault(), ClearSelectedSession: false);
			}
		}
		else if (action == NotifyCollectionChangedAction.Replace && ContainsReference(oldSnapshot, currentSelectedTab))
		{
			return new ShellTabSelectionResult(
				newItems?.FirstOrDefault() ?? snapshot.FirstOrDefault(),
				ClearSelectedSession: false);
		}

		return new ShellTabSelectionResult(currentSelectedTab, ClearSelectedSession: false);
	}

	public ShellTabSelectionResult ResolveAfterTabRemoval(
		IEnumerable<object?> tabs,
		object? currentSelectedTab,
		object? removedItem)
	{
		var snapshot = tabs.ToList();
		if (snapshot.Count == 0)
		{
			return new ShellTabSelectionResult(null, ClearSelectedSession: true);
		}

		if (currentSelectedTab == null || ReferenceEquals(removedItem, currentSelectedTab))
		{
			return new ShellTabSelectionResult(snapshot[0], ClearSelectedSession: false);
		}

		return new ShellTabSelectionResult(currentSelectedTab, ClearSelectedSession: false);
	}

	private static bool ContainsReference(IEnumerable<object?>? items, object? target)
	{
		return target != null && items != null && items.Any(item => ReferenceEquals(item, target));
	}
}
