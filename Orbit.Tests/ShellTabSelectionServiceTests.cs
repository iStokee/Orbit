using System.Collections.Generic;
using System.Collections.Specialized;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellTabSelectionServiceTests
{
	[Fact]
	public void ResolveAfterCollectionChanged_ClearsSelectionWhenLastTabRemoved()
	{
		var service = new ShellTabSelectionService();
		var removed = new object();

		var result = service.ResolveAfterCollectionChanged(
			tabs: new List<object>(),
			currentSelectedTab: removed,
			NotifyCollectionChangedAction.Remove,
			oldItems: new[] { removed },
			newItems: null);

		Assert.Null(result.SelectedTab);
		Assert.True(result.ClearSelectedSession);
	}

	[Fact]
	public void ResolveAfterCollectionChanged_SelectsFirstRemainingTabWhenSelectedTabWasRemoved()
	{
		var service = new ShellTabSelectionService();
		var removed = new object();
		var remaining = new object();

		var result = service.ResolveAfterCollectionChanged(
			tabs: new[] { remaining },
			currentSelectedTab: removed,
			NotifyCollectionChangedAction.Remove,
			oldItems: new[] { removed },
			newItems: null);

		Assert.Same(remaining, result.SelectedTab);
		Assert.False(result.ClearSelectedSession);
	}

	[Fact]
	public void ResolveAfterCollectionChanged_KeepsSelectionWhenUnselectedTabWasRemoved()
	{
		var service = new ShellTabSelectionService();
		var selected = new object();
		var removed = new object();

		var result = service.ResolveAfterCollectionChanged(
			tabs: new[] { selected },
			currentSelectedTab: selected,
			NotifyCollectionChangedAction.Remove,
			oldItems: new[] { removed },
			newItems: null);

		Assert.Same(selected, result.SelectedTab);
		Assert.False(result.ClearSelectedSession);
	}

	[Fact]
	public void ResolveAfterCollectionChanged_SelectsReplacementWhenSelectedTabWasReplaced()
	{
		var service = new ShellTabSelectionService();
		var oldSelected = new object();
		var replacement = new object();

		var result = service.ResolveAfterCollectionChanged(
			tabs: new[] { replacement },
			currentSelectedTab: oldSelected,
			NotifyCollectionChangedAction.Replace,
			oldItems: new[] { oldSelected },
			newItems: new[] { replacement });

		Assert.Same(replacement, result.SelectedTab);
		Assert.False(result.ClearSelectedSession);
	}

	[Fact]
	public void ResolveAfterTabRemoval_SelectsFirstTabWhenCurrentSelectionWasRemoved()
	{
		var service = new ShellTabSelectionService();
		var removed = new object();
		var remaining = new object();

		var result = service.ResolveAfterTabRemoval(new[] { remaining }, removed, removed);

		Assert.Same(remaining, result.SelectedTab);
		Assert.False(result.ClearSelectedSession);
	}

	[Fact]
	public void ResolveAfterTabRemoval_ClearsSelectionWhenNoTabsRemain()
	{
		var service = new ShellTabSelectionService();
		var removed = new object();

		var result = service.ResolveAfterTabRemoval(new List<object>(), removed, removed);

		Assert.Null(result.SelectedTab);
		Assert.True(result.ClearSelectedSession);
	}
}
