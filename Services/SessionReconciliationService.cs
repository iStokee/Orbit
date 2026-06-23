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
using static Orbit.Utilities.VisualTreeUtil;

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

	/// <summary>
	/// True when the given window data-context is a registered Orbit-View tear-off host. Registry
	/// lookup only — no visual-tree scrape (Stage 2c replacement for the CaptureUiOwnership-based
	/// window classification).
	/// </summary>
	public bool IsOrbitViewHost(object? windowDataContext)
	{
		if (windowDataContext == null)
		{
			return false;
		}

		return _tearOffRegistry
			.GetHosts("OrbitMainShell", TearOffHostRegistry.HostOrigin.OrbitView)
			.Any(host => ReferenceEquals(host.Window.DataContext, windowDataContext));
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

	/// <summary>
	/// Cheap placement-model check (no visual-tree scrape) for whether a drag/move grace is open
	/// for the session. Reconcile passes must consult this before re-homing or restoring, so they
	/// don't act on the transiently-inconsistent visual tree mid-move and create duplicate tabs.
	/// </summary>
	public bool IsSessionMoving(SessionModel session)
		=> session != null && _placement.IsMoveInProgress(session);

	/// <summary>
	/// Placement-driven "is this workspace item currently hosted in a non-Orbit tab strip" for
	/// sessions and tools (Stage 2e). Replaces the reconcile loop's visual-tree scrape read.
	/// </summary>
	public bool IsItemInNonOrbitHost(object item) => item switch
	{
		SessionModel session => _placement.IsInNonOrbitHost(session),
		ToolTabItem tool => _placement.IsInNonOrbitHost(tool),
		_ => false
	};

	/// <summary>
	/// Count of live sessions the authoritative placement model reports in no host (excluding
	/// closing/terminal). Placement-driven health metric replacing the scrape-based conflict count
	/// (conflicts are now resolved by the ownership coordinator). No visual-tree walk.
	/// </summary>
	public int CountUnplacedSessions()
		=> _sessionCollection.Sessions.Count(session =>
			session.State is not SessionState.Closed and not SessionState.ShuttingDown &&
			!_placement.IsPlacedInHost(session));

	/// <summary>
	/// Placement-driven orphan decision (Stage 2c) — the data-backed replacement for the
	/// snapshot/scrape overload. A session should be restored only when it is tracked, settled
	/// (not moving), not closing/terminal, and the authoritative placement says it is in no host.
	/// </summary>
	public bool ShouldRestoreOrphan(SessionModel session)
	{
		if (session == null ||
			!_sessionCollection.Sessions.Contains(session) ||
			_placement.IsMoveInProgress(session) ||
			_placement.GetPlacement(session) == SessionPlacementKind.Closing ||
			session.State is SessionState.Closed or SessionState.ShuttingDown)
		{
			return false;
		}

		return !_placement.IsPlacedInHost(session);
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
	public bool IsInNonOrbitTabs(object item) => NonOrbitItems.Contains(item);
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
