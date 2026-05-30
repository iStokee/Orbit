using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

/// <summary>
/// Coordinates movement between the main tab shell, split tab owners, and the
/// Orbit workspace. View models should delegate placement changes here instead
/// of directly mutating multiple UI collections.
/// </summary>
public sealed class SessionUiCoordinatorService
{
	private readonly SessionPlacementService _placement;
	private readonly SessionReconciliationService _reconciliation;
	private readonly OrbitLayoutStateService _orbitLayoutState;

	public SessionUiCoordinatorService(
		SessionPlacementService placement,
		SessionReconciliationService reconciliation,
		OrbitLayoutStateService orbitLayoutState)
	{
		_placement = placement ?? throw new ArgumentNullException(nameof(placement));
		_reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
		_orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));
	}

	public bool MoveItemToOrbit(
		object item,
		Action<object>? onRemovedFromTabShell = null,
		object? toolDataContext = null,
		string reason = "move-to-orbit")
	{
		if (item == null)
		{
			return false;
		}

		if (item is ToolTabItem orbitTool && string.Equals(orbitTool.Key, "OrbitView", StringComparison.Ordinal))
		{
			return false;
		}

		if (item is SessionModel session)
		{
			using (_placement.BeginMove(session, SessionPlacementKind.OrbitWorkspace))
			{
				RemoveFromTabShell(item, SessionTabOwnerScope.NonOrbitOnly, onRemovedFromTabShell, reason);
				_orbitLayoutState.AddItem(session);
			}

			return true;
		}

		if (item is ToolTabItem tool)
		{
			RemoveFromTabShell(tool, SessionTabOwnerScope.NonOrbitOnly, onRemovedFromTabShell, reason);
			EnsureToolDataContext(tool, toolDataContext);
			_orbitLayoutState.AddItem(tool);
			return true;
		}

		return false;
	}

	public SessionModel? AdoptSessionsIntoOrbit(
		IEnumerable<SessionModel> sessions,
		Action<object>? onRemovedFromTabShell = null,
		string reason = "adopt-sessions-to-orbit")
	{
		SessionModel? firstMoved = null;
		foreach (var session in sessions.Where(session => session != null).ToList())
		{
			if (MoveItemToOrbit(session, onRemovedFromTabShell, reason: reason))
			{
				firstMoved ??= session;
			}
		}

		return firstMoved;
	}

	public bool MoveSessionToMainTabs(
		SessionModel session,
		ObservableCollection<object> mainTabs,
		string reason = "move-session-to-main-tabs")
	{
		if (session == null || mainTabs == null)
		{
			return false;
		}

		using (_placement.BeginMove(session, SessionPlacementKind.MainTabs))
		{
			_orbitLayoutState.RemoveItem(session);
			_reconciliation.RemoveItemFromTabOwners(session, SessionTabOwnerScope.OrbitOnly, reason);

			if (!mainTabs.Contains(session))
			{
				mainTabs.Add(session);
			}
		}

		return true;
	}

	public int RestoreOrbitWorkspaceToTabs(
		ObservableCollection<object> mainTabs,
		object? toolDataContext = null,
		string reason = "restore-orbit-workspace")
	{
		if (mainTabs == null)
		{
			return 0;
		}

		var restored = 0;
		foreach (var item in _orbitLayoutState.Items.ToList())
		{
			switch (item)
			{
				case SessionModel session:
					if (MoveSessionToMainTabs(session, mainTabs, $"{reason}-session"))
					{
						restored++;
					}
					break;
				case ToolTabItem tool:
					if (string.Equals(tool.Key, "OrbitView", StringComparison.Ordinal))
					{
						continue;
					}

					_orbitLayoutState.RemoveItem(tool);
					_reconciliation.RemoveItemFromTabOwners(tool, SessionTabOwnerScope.OrbitOnly, $"{reason}-tool");
					EnsureToolDataContext(tool, toolDataContext);
					if (!mainTabs.Contains(tool))
					{
						mainTabs.Add(tool);
					}
					restored++;
					break;
			}
		}

		return restored;
	}

	public bool RemoveSessionFromVisibleShell(
		SessionModel session,
		Action<object>? onRemovedFromTabShell = null,
		string reason = "remove-session-visible-shell")
	{
		if (session == null)
		{
			return false;
		}

		_orbitLayoutState.RemoveItem(session);
		return RemoveFromTabShell(session, SessionTabOwnerScope.All, onRemovedFromTabShell, reason);
	}

	private bool RemoveFromTabShell(
		object item,
		SessionTabOwnerScope scope,
		Action<object>? onRemovedFromTabShell,
		string reason)
	{
		var removed = _reconciliation.RemoveItemFromTabOwners(item, scope, reason);
		if (removed)
		{
			onRemovedFromTabShell?.Invoke(item);
		}

		return removed;
	}

	private static void EnsureToolDataContext(ToolTabItem tool, object? dataContext)
	{
		if (tool?.HostControl == null || dataContext == null || ReferenceEquals(tool.HostControl.DataContext, dataContext))
		{
			return;
		}

		tool.HostControl.DataContext = dataContext;
	}
}
