using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using Dragablz;
using Dragablz.Dockablz;
using static Orbit.Utilities.VisualTreeUtil;

namespace Orbit.Services;

/// <summary>
/// Collapses empty Dragablz layout branches. Dragablz consolidates branches automatically via
/// its drag <c>TabEmptiedHandler</c>, but a programmatic removal (e.g. closing a session) does
/// not go through that path, leaving "stuck" empty cells. This is the single owner of the
/// branch-consolidation reflection that both the Orbit workspace and the main shell use.
/// </summary>
public static class LayoutBranchConsolidator
{
	private static readonly Lazy<MethodInfo?> ConsolidateBranchMethod = new(() =>
		typeof(Layout).GetMethod("ConsolidateBranch", BindingFlags.Static | BindingFlags.NonPublic));

	/// <summary>
	/// Consolidates the branch containing <paramref name="control"/> if it is empty. No-op when
	/// the control still has items. Returns true if a consolidation was attempted.
	/// </summary>
	public static bool CollapseIfEmpty(TabablzControl control)
	{
		if (control == null || control.Items.Count > 0)
		{
			return false;
		}

		try
		{
			ConsolidateBranchMethod.Value?.Invoke(null, new object[] { (DependencyObject)control });
			return true;
		}
		catch
		{
			return false; // best effort — reflection target is Dragablz-internal
		}
	}

	/// <summary>
	/// Collapses every empty branch tab control inside a layout, while always preserving the
	/// final/root control so the host window stays alive (matches the primary-shell rule that a
	/// branch is only collapsed when more than one tab control exists). Returns the count collapsed.
	/// </summary>
	public static int CollapseEmptyBranches(Layout layout)
	{
		if (layout == null)
		{
			return 0;
		}

		var collapsed = 0;
		// Each consolidation reshapes the visual tree, so re-query rather than iterate a stale
		// snapshot. The guard bound prevents any pathological loop.
		for (var guard = 0; guard < 32; guard++)
		{
			var controls = FindVisualChildren<TabablzControl>(layout).ToList();
			if (controls.Count <= 1)
			{
				break;
			}

			var empty = controls.FirstOrDefault(control => control.Items.Count == 0);
			if (empty == null || !CollapseIfEmpty(empty))
			{
				break;
			}

			collapsed++;
		}

		return collapsed;
	}

	/// <summary>
	/// Collapses empty branches across every layout hosted in <paramref name="window"/>.
	/// </summary>
	public static int CollapseEmptyBranchesInWindow(Window window)
	{
		if (window == null)
		{
			return 0;
		}

		return FindVisualChildren<Layout>(window).Sum(CollapseEmptyBranches);
	}
}
