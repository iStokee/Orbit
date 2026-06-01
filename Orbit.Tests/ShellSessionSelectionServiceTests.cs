using System.Collections.Generic;
using System.Collections.Specialized;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellSessionSelectionServiceTests
{
	[Fact]
	public void ResolveHotReloadTargetAfterSelectionChanged_UsesSelectedSessionWhenCurrentTargetMissing()
	{
		var service = new ShellSessionSelectionService();
		var selected = NewSession("Selected");

		var target = service.ResolveHotReloadTargetAfterSelectionChanged(new[] { selected }, selected, null);

		Assert.Same(selected, target);
	}

	[Fact]
	public void ResolveHotReloadTargetAfterSelectionChanged_ReplacesStaleTargetWithSelectedSession()
	{
		var service = new ShellSessionSelectionService();
		var selected = NewSession("Selected");
		var stale = NewSession("Stale");

		var target = service.ResolveHotReloadTargetAfterSelectionChanged(new[] { selected }, selected, stale);

		Assert.Same(selected, target);
	}

	[Fact]
	public void ResolveHotReloadTargetAfterSelectionChanged_ReplacesStaleTargetWithFirstSessionWhenNoSelection()
	{
		var service = new ShellSessionSelectionService();
		var first = NewSession("First");
		var stale = NewSession("Stale");

		var target = service.ResolveHotReloadTargetAfterSelectionChanged(new[] { first }, null, stale);

		Assert.Same(first, target);
	}

	[Fact]
	public void ResolveAfterSessionsChanged_ReplacesRemovedSelectedAndHotReloadSessionsWithFirstSession()
	{
		var service = new ShellSessionSelectionService();
		var first = NewSession("First");
		var removedSelected = NewSession("Removed Selected");
		var removedHotReload = NewSession("Removed Hot Reload");

		var result = service.ResolveAfterSessionsChanged(
			new[] { first },
			NotifyCollectionChangedAction.Remove,
			removedSelected,
			removedHotReload);

		Assert.Same(first, result.SelectedSession);
		Assert.Same(first, result.HotReloadTargetSession);
	}

	[Fact]
	public void ResolveAfterSessionsChanged_SelectsFirstSessionWhenAddingIntoEmptySelection()
	{
		var service = new ShellSessionSelectionService();
		var first = NewSession("First");

		var result = service.ResolveAfterSessionsChanged(
			new[] { first },
			NotifyCollectionChangedAction.Add,
			selectedSession: null,
			hotReloadTargetSession: null);

		Assert.Same(first, result.SelectedSession);
		Assert.Same(first, result.HotReloadTargetSession);
	}

	[Fact]
	public void ResolveAfterSessionsChanged_KeepsExistingValidSelections()
	{
		var service = new ShellSessionSelectionService();
		var selected = NewSession("Selected");
		var hotReload = NewSession("Hot Reload");
		var first = NewSession("First");

		var result = service.ResolveAfterSessionsChanged(
			new[] { first, selected, hotReload },
			NotifyCollectionChangedAction.Add,
			selected,
			hotReload);

		Assert.Same(selected, result.SelectedSession);
		Assert.Same(hotReload, result.HotReloadTargetSession);
	}

	private static SessionModel NewSession(string name)
	{
		return new SessionModel { Name = name };
	}
}
