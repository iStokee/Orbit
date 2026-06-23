using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Orbit.Models;
using Application = System.Windows.Application;

namespace Orbit.Services;

/// <summary>
/// Enforces the single-host invariant: a session is shown in exactly one place — the host that
/// matches its authoritative <see cref="SessionPlacementService"/> placement. Reacting to
/// <see cref="SessionPlacementService.PlacementChanged"/>, it resolves the "ghost duplicate"
/// (a session visible in both an Orbit cell and a non-Orbit tab strip) by evicting the copy that
/// does NOT match placement.
///
/// Prior to this, conflicting ownership was only ever logged ("leaving cleanup to workspace
/// reconciliation") and never actually resolved. This is the resolver.
///
/// Safety: enforcement runs deferred (Background), never during an active drag, never while a
/// move is in progress, and ONLY when a real conflict exists — so it can only remove a redundant
/// duplicate, never the last remaining copy of a session.
/// </summary>
public sealed class SessionOwnershipCoordinatorService : IDisposable
{
	private readonly SessionPlacementService _placement;
	private readonly SessionReconciliationService _reconciliation;
	private readonly OrbitLayoutStateService _orbitLayoutState;
	private bool _disposed;

	public SessionOwnershipCoordinatorService(
		SessionPlacementService placement,
		SessionReconciliationService reconciliation,
		OrbitLayoutStateService orbitLayoutState)
	{
		_placement = placement ?? throw new ArgumentNullException(nameof(placement));
		_reconciliation = reconciliation ?? throw new ArgumentNullException(nameof(reconciliation));
		_orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));

		_placement.PlacementChanged += OnPlacementChanged;
	}

	/// <summary>
	/// Which owners to evict so the session is left only in the host matching its placement.
	/// Returns null for placements that are not a concrete host (Unknown / Closing) — nothing to
	/// enforce. Pure and deterministic for unit testing.
	/// </summary>
	internal static SessionTabOwnerScope? ResolveEvictionScope(SessionPlacementKind placement)
		=> placement switch
		{
			SessionPlacementKind.OrbitWorkspace => SessionTabOwnerScope.NonOrbitOnly,
			SessionPlacementKind.MainTabs => SessionTabOwnerScope.OrbitOnly,
			SessionPlacementKind.TearOffWindow => SessionTabOwnerScope.OrbitOnly,
			_ => null,
		};

	private void OnPlacementChanged(object? sender, SessionPlacementChangedEventArgs e)
	{
		if (_disposed || e?.Session == null || ResolveEvictionScope(e.Current) is null)
		{
			return;
		}

		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null)
		{
			EnforceSingleHost(e.Session, e.Reason);
			return;
		}

		dispatcher.BeginInvoke(
			new Action(() => EnforceSingleHost(e.Session, e.Reason)),
			DispatcherPriority.Background);
	}

	private void EnforceSingleHost(SessionModel session, string? reason)
	{
		if (_disposed || session == null)
		{
			return;
		}

		// Wait until the move settles and never fight an in-flight drag.
		if (_placement.IsMoveInProgress(session) || Mouse.LeftButton == MouseButtonState.Pressed)
		{
			return;
		}

		var scope = ResolveEvictionScope(_placement.GetPlacement(session));
		if (scope is null)
		{
			return;
		}

		var orbitItems = _orbitLayoutState.Items.ToList();
		var snapshot = _reconciliation.Capture(session, orbitItems, reason ?? "enforce-single-host");

		// Only act on a genuine conflict (visible in both Orbit and non-Orbit). This is what
		// guarantees we only ever remove a redundant duplicate, never the last copy.
		if (!snapshot.HasConflictingUiOwnership)
		{
			return;
		}

		var placement = _placement.GetPlacement(session);
		if (_reconciliation.RemoveItemFromTabOwners(session, scope.Value, $"enforce-single-host:{placement}"))
		{
			_reconciliation.LogDecision(
				"ownership-conflict-resolved",
				_reconciliation.Capture(session, _orbitLayoutState.Items.ToList(), reason ?? "enforce-single-host"),
				$"kept placement={placement}, evicted scope={scope}");
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_placement.PlacementChanged -= OnPlacementChanged;
	}
}
