using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Dragablz;
using Orbit.Models;
using Orbit.ViewModels;
using Orbit.Views;
using Application = System.Windows.Application;

namespace Orbit.Services;

/// <summary>
/// Builds a compact view of where Orbit believes a session exists. This keeps
/// lifetime decisions separate from transient Dragablz visual state.
/// </summary>
public sealed class SessionReconciliationService
{
	private readonly SessionCollectionService _sessionCollection;
	private readonly SessionPlacementService _placement;
	private readonly TearOffHostRegistry _tearOffRegistry;

	public SessionReconciliationService(
		SessionCollectionService sessionCollection,
		SessionPlacementService placement,
		TearOffHostRegistry tearOffRegistry)
	{
		_sessionCollection = sessionCollection ?? throw new ArgumentNullException(nameof(sessionCollection));
		_placement = placement ?? throw new ArgumentNullException(nameof(placement));
		_tearOffRegistry = tearOffRegistry ?? throw new ArgumentNullException(nameof(tearOffRegistry));
	}

	public SessionReconciliationSnapshot Capture(
		SessionModel session,
		IEnumerable<object>? orbitWorkspaceItems = null,
		string? reason = null)
	{
		if (session == null)
		{
			throw new ArgumentNullException(nameof(session));
		}

		var workspaceItems = orbitWorkspaceItems?.ToList() ?? new List<object>();
		var uiOwnership = CaptureUiOwnership(workspaceItems);
		var tabOwnership = FindTabOwners(session, uiOwnership.OrbitWindows);
		var inWorkspace = workspaceItems.Any(item => ReferenceEquals(item, session));
		var inCollection = _sessionCollection.Sessions.Contains(session);
		var processRunning = IsProcessRunning(session.RSProcess);

		return new SessionReconciliationSnapshot(
			session.Id,
			session.Name ?? session.Id.ToString(),
			session.SessionType,
			session.State,
			session.InjectionState,
			_placement.GetPlacement(session),
			_placement.IsMoveInProgress(session),
			inCollection,
			inWorkspace,
			tabOwnership.AnyTabOwnerCount > 0,
			tabOwnership.OrbitTabOwnerCount > 0,
			tabOwnership.NonOrbitTabOwnerCount > 0,
			tabOwnership.Owners,
			processRunning,
			session.RSProcess?.Id,
			session.ExternalHandle,
			reason);
	}

	public SessionUiOwnership CaptureUiOwnership(
		IEnumerable<object>? orbitWorkspaceItems = null,
		string partition = "OrbitMainShell")
	{
		var orbitItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
		var nonOrbitItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
		var tabStripItems = new HashSet<object>(ReferenceEqualityComparer.Instance);
		var orbitWindows = new HashSet<Window>(
			_tearOffRegistry.GetHosts(partition, TearOffHostRegistry.HostOrigin.OrbitView)
				.Select(host => host.Window));

		foreach (var item in orbitWorkspaceItems ?? Enumerable.Empty<object>())
		{
			if (item != null)
			{
				orbitItems.Add(item);
			}
		}

		var windows = Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel vm)
			{
				continue;
			}

			var isOrbitWindow = orbitWindows.Contains(window);
			foreach (var item in vm.Tabs.Where(item => item != null))
			{
				tabStripItems.Add(item);
				if (isOrbitWindow)
				{
					orbitItems.Add(item);
				}
				else
				{
					nonOrbitItems.Add(item);
				}
			}

			foreach (var tabControl in FindVisualChildren<TabablzControl>(window))
			{
				var isOrbitTabControl = isOrbitWindow || FindVisualAncestor<OrbitGridLayoutView>(tabControl) != null;
				foreach (var item in tabControl.Items.Cast<object>().Where(item => item != null))
				{
					tabStripItems.Add(item);
					if (isOrbitTabControl)
					{
						orbitItems.Add(item);
					}
					else
					{
						nonOrbitItems.Add(item);
					}
				}
			}
		}

		return new SessionUiOwnership(orbitItems, nonOrbitItems, tabStripItems, orbitWindows);
	}

	public bool ShouldRestoreOrphan(SessionReconciliationSnapshot snapshot)
	{
		if (!snapshot.InSessionCollection ||
			snapshot.IsMoveInProgress ||
			snapshot.Placement == SessionPlacementKind.Closing ||
			snapshot.State is SessionState.Closed or SessionState.ShuttingDown)
		{
			return false;
		}

		return !snapshot.HasAnyUiReference;
	}

	public IReadOnlyList<SessionReconciliationSnapshot> CaptureAll(
		IEnumerable<object>? orbitWorkspaceItems = null,
		string? reason = null)
	{
		var workspaceItems = orbitWorkspaceItems?.ToList() ?? new List<object>();
		return _sessionCollection.Sessions
			.Select(session => Capture(session, workspaceItems, reason))
			.ToArray();
	}

	public void LogDiagnostics(
		IEnumerable<object>? orbitWorkspaceItems = null,
		string? reason = null)
	{
		var workspaceItems = orbitWorkspaceItems?.ToList() ?? new List<object>();
		var ownership = CaptureUiOwnership(workspaceItems);
		var snapshots = CaptureAll(workspaceItems, reason);
		var header =
			$"[Orbit][Reconcile] diagnostics reason='{reason ?? "manual"}' sessions={snapshots.Count} orbitItems={ownership.OrbitItems.Count} nonOrbitItems={ownership.NonOrbitItems.Count} tabItems={ownership.TabStripItems.Count} orbitTearOffWindows={ownership.OrbitWindows.Count}";
		Console.WriteLine(header);
		OrbitInteractionLogger.Log(header);

		foreach (var snapshot in snapshots)
		{
			LogDecision("diagnostic-session", snapshot);
		}
	}

	public bool RemoveItemFromTabOwners(
		object item,
		SessionTabOwnerScope scope,
		string? reason = null)
	{
		if (item == null)
		{
			return false;
		}

		var removedAny = false;
		var orbitWindows = new HashSet<Window>(
			_tearOffRegistry.GetHosts("OrbitMainShell", TearOffHostRegistry.HostOrigin.OrbitView)
				.Select(host => host.Window));
		var windows = (Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>()).ToList();

		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel vm)
			{
				continue;
			}

			var windowIsOrbit = orbitWindows.Contains(window);
			if (ShouldIncludeOwner(windowIsOrbit, scope) && vm.Tabs.Contains(item))
			{
				vm.Tabs.Remove(item);
				removedAny = true;
			}

			foreach (var tabControl in FindVisualChildren<TabablzControl>(window).ToList())
			{
				var tabControlIsOrbit = windowIsOrbit || FindVisualAncestor<OrbitGridLayoutView>(tabControl) != null;
				if (!ShouldIncludeOwner(tabControlIsOrbit, scope) ||
					tabControl.ItemsSource != null ||
					!tabControl.Items.Contains(item))
				{
					continue;
				}

				tabControl.Items.Remove(item);
				removedAny = true;
			}
		}

		if (removedAny)
		{
			var message = $"[Orbit][Reconcile] removed tab owner itemType={item.GetType().Name} scope={scope}{FormatReason(reason)}";
			Console.WriteLine(message);
			OrbitInteractionLogger.Log(message);
		}

		return removedAny;
	}

	public void LogDecision(string action, SessionReconciliationSnapshot snapshot, string? detail = null)
	{
		var suffix = string.IsNullOrWhiteSpace(detail) ? string.Empty : $" detail='{detail.Trim()}'";
		var message = $"[Orbit][Reconcile] {action}: {snapshot.ToLogString()}{suffix}";
		Console.WriteLine(message);
		OrbitInteractionLogger.Log(message);
	}

	private static TabOwnership FindTabOwners(SessionModel session, IReadOnlySet<Window> orbitWindows)
	{
		var owners = new List<string>();
		var orbitCount = 0;
		var nonOrbitCount = 0;
		var windows = Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel vm)
			{
				continue;
			}

			var title = string.IsNullOrWhiteSpace(window.Title) ? window.GetType().Name : window.Title;
			if (vm.Tabs.Contains(session))
			{
				var origin = orbitWindows.Contains(window) ? "orbit" : "main";
				if (origin == "orbit")
				{
					orbitCount++;
				}
				else
				{
					nonOrbitCount++;
				}

				owners.Add($"{origin}:{title}:{RuntimeHelpers.GetHashCode(window):X}:vm-tabs");
			}

			foreach (var tabControl in FindVisualChildren<TabablzControl>(window))
			{
				if (!tabControl.Items.Cast<object>().Any(item => ReferenceEquals(item, session)))
				{
					continue;
				}

				var isOrbitTabControl = orbitWindows.Contains(window) || FindVisualAncestor<OrbitGridLayoutView>(tabControl) != null;
				if (isOrbitTabControl)
				{
					orbitCount++;
				}
				else
				{
					nonOrbitCount++;
				}

				var origin = isOrbitTabControl ? "orbit" : "main";
				owners.Add($"{origin}:{title}:{RuntimeHelpers.GetHashCode(window):X}:tab-control:{RuntimeHelpers.GetHashCode(tabControl):X}");
			}
		}

		return new TabOwnership(owners, orbitCount, nonOrbitCount);
	}

	private static T? FindVisualAncestor<T>(DependencyObject child) where T : DependencyObject
	{
		var current = child;
		while (current != null)
		{
			if (current is T typed)
			{
				return typed;
			}

			current = VisualTreeHelper.GetParent(current);
		}

		return null;
	}

	private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
	{
		if (parent == null)
		{
			yield break;
		}

		var childCount = VisualTreeHelper.GetChildrenCount(parent);
		for (var index = 0; index < childCount; index++)
		{
			var child = VisualTreeHelper.GetChild(parent, index);
			if (child is T typed)
			{
				yield return typed;
			}

			foreach (var descendant in FindVisualChildren<T>(child))
			{
				yield return descendant;
			}
		}
	}

	private sealed record TabOwnership(
		IReadOnlyList<string> Owners,
		int OrbitTabOwnerCount,
		int NonOrbitTabOwnerCount)
	{
		public int AnyTabOwnerCount => OrbitTabOwnerCount + NonOrbitTabOwnerCount;
	}

	private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
	{
		public static ReferenceEqualityComparer Instance { get; } = new();

		public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

		public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
	}

	private static bool IsProcessRunning(Process? process)
	{
		if (process == null)
		{
			return false;
		}

		try
		{
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}

	private static bool ShouldIncludeOwner(bool ownerIsOrbit, SessionTabOwnerScope scope)
		=> scope switch
		{
			SessionTabOwnerScope.All => true,
			SessionTabOwnerScope.OrbitOnly => ownerIsOrbit,
			SessionTabOwnerScope.NonOrbitOnly => !ownerIsOrbit,
			_ => false
		};

	private static string FormatReason(string? reason)
		=> string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason.Trim()})";
}

public enum SessionTabOwnerScope
{
	All = 0,
	OrbitOnly = 1,
	NonOrbitOnly = 2
}

public sealed record SessionUiOwnership(
	IReadOnlySet<object> OrbitItems,
	IReadOnlySet<object> NonOrbitItems,
	IReadOnlySet<object> TabStripItems,
	IReadOnlySet<Window> OrbitWindows)
{
	public bool IsInOrbit(object item) => OrbitItems.Contains(item);

	public bool IsInNonOrbitTabs(object item) => NonOrbitItems.Contains(item);

	public bool IsInAnyTabStrip(object item) => TabStripItems.Contains(item);
}

public sealed record SessionReconciliationSnapshot(
	Guid SessionId,
	string Name,
	SessionType SessionType,
	SessionState State,
	InjectionState InjectionState,
	SessionPlacementKind Placement,
	bool IsMoveInProgress,
	bool InSessionCollection,
	bool InOrbitWorkspace,
	bool InAnyTabStrip,
	bool InOrbitTabStrip,
	bool InNonOrbitTabStrip,
	IReadOnlyList<string> TabOwners,
	bool ProcessRunning,
	int? ProcessId,
	nint ExternalHandle,
	string? Reason)
{
	public bool HasAnyUiReference => InOrbitWorkspace || InAnyTabStrip;

	public bool HasConflictingUiOwnership =>
		InNonOrbitTabStrip && (InOrbitWorkspace || InOrbitTabStrip);

	public string ToLogString()
	{
		var pid = ProcessId?.ToString() ?? "n/a";
		var hwnd = ExternalHandle == nint.Zero ? "n/a" : $"0x{ExternalHandle:X}";
		var owners = TabOwners.Count == 0 ? "none" : string.Join("|", TabOwners);
		var reason = string.IsNullOrWhiteSpace(Reason) ? string.Empty : $" reason='{Reason.Trim()}'";
		return $"session='{Name}' id={SessionId:N} type={SessionType} state={State}/{InjectionState} placement={Placement} moving={IsMoveInProgress} collection={InSessionCollection} orbitWorkspace={InOrbitWorkspace} orbitTabs={InOrbitTabStrip} nonOrbitTabs={InNonOrbitTabStrip} tabs={InAnyTabStrip} conflict={HasConflictingUiOwnership} tabOwners={owners} processRunning={ProcessRunning} pid={pid} hwnd={hwnd}{reason}";
	}
}
