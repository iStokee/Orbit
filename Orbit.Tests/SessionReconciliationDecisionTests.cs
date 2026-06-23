using System;
using System.Collections.Generic;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

/// <summary>
/// Characterization tests for the pure orphan-restore decision
/// (<see cref="SessionReconciliationService.ShouldRestoreOrphan"/>). This is the logic that
/// mis-fires mid-drag today and produces duplicated/lost sessions. Pinning the full branch
/// matrix guards the Cluster 1 Stage 1 (gate on IsMoveInProgress) and Stage 2 (delete the
/// visual-tree scrape) refactors.
/// </summary>
public sealed class SessionReconciliationDecisionTests
{
	[Fact]
	public void TrackedSession_WithNoUiReference_AndNotMoving_IsRestored()
	{
		// The one case that SHOULD restore: a live, tracked session that has fallen out of
		// every UI host while no move is in flight.
		Assert.True(Decide(Snapshot()));
	}

	[Fact]
	public void SessionWithAnyTabStripReference_IsNotRestored()
	{
		Assert.False(Decide(Snapshot(inAnyTabStrip: true)));
	}

	[Fact]
	public void SessionInOrbitWorkspace_IsNotRestored()
	{
		Assert.False(Decide(Snapshot(inOrbitWorkspace: true)));
	}

	[Fact]
	public void MoveInProgress_SuppressesRestore_EvenWithNoUiReference()
	{
		// This is the guard Stage 1 relies on: while a Dragablz move is in flight the visual
		// tree transiently shows no owner, but we must NOT treat that as an orphan.
		Assert.False(Decide(Snapshot(moving: true)));
	}

	[Fact]
	public void NotInSessionCollection_IsNotRestored()
	{
		Assert.False(Decide(Snapshot(inCollection: false)));
	}

	[Fact]
	public void ClosingPlacement_IsNotRestored()
	{
		Assert.False(Decide(Snapshot(placement: SessionPlacementKind.Closing)));
	}

	[Theory]
	[InlineData(SessionState.Closed)]
	[InlineData(SessionState.ShuttingDown)]
	public void TerminalLifecycleStates_AreNotRestored(SessionState state)
	{
		Assert.False(Decide(Snapshot(state: state)));
	}

	[Fact]
	public void IsSessionMoving_ReflectsExternalMoveGrace()
	{
		// The Orbit reconcile loop gates re-homing on this; it must track the drag-out grace.
		var placement = new SessionPlacementService();
		var service = new SessionReconciliationService(
			SessionCollectionService.Instance, placement, new TearOffHostRegistry());
		var session = new SessionModel { Name = "Drag" };

		Assert.False(service.IsSessionMoving(session));
		placement.BeginExternalMove(session);
		Assert.True(service.IsSessionMoving(session));
		placement.EndExternalMove(session);
		Assert.False(service.IsSessionMoving(session));
	}

	// --- Stage 2c: placement-driven orphan decision (no scrape) -----------------------------

	[Fact]
	public void ShouldRestoreOrphanSession_RestoresWhenTrackedAndUnplaced()
	{
		var placement = new SessionPlacementService();
		var service = new SessionReconciliationService(
			SessionCollectionService.Instance, placement, new TearOffHostRegistry());
		var session = new SessionModel { Name = "Orphan" };

		try
		{
			SessionCollectionService.Instance.Sessions.Add(session);

			// Tracked, settled, placement Unknown (in no host) → restore.
			Assert.True(service.ShouldRestoreOrphan(session));

			placement.SetPlacement(session, SessionPlacementKind.MainTabs);
			Assert.False(service.ShouldRestoreOrphan(session)); // placed in a host

			placement.SetPlacement(session, SessionPlacementKind.Closing);
			Assert.False(service.ShouldRestoreOrphan(session)); // closing
		}
		finally
		{
			SessionCollectionService.Instance.Sessions.Remove(session);
		}
	}

	[Fact]
	public void ShouldRestoreOrphanSession_NotRestoredWhenUntrackedOrMoving()
	{
		var placement = new SessionPlacementService();
		var service = new SessionReconciliationService(
			SessionCollectionService.Instance, placement, new TearOffHostRegistry());
		var session = new SessionModel { Name = "Edge" };

		// Not in the session collection → never restore.
		Assert.False(service.ShouldRestoreOrphan(session));

		try
		{
			SessionCollectionService.Instance.Sessions.Add(session);
			placement.BeginExternalMove(session);
			Assert.False(service.ShouldRestoreOrphan(session)); // mid-move grace
		}
		finally
		{
			SessionCollectionService.Instance.Sessions.Remove(session);
		}
	}

	private static bool Decide(SessionReconciliationSnapshot snapshot)
	{
		var service = new SessionReconciliationService(
			SessionCollectionService.Instance,
			new SessionPlacementService(),
			new TearOffHostRegistry());
		return service.ShouldRestoreOrphan(snapshot);
	}

	private static SessionReconciliationSnapshot Snapshot(
		bool inCollection = true,
		bool moving = false,
		SessionPlacementKind placement = SessionPlacementKind.MainTabs,
		SessionState state = SessionState.ClientReady,
		bool inOrbitWorkspace = false,
		bool inAnyTabStrip = false,
		bool inOrbitTabStrip = false,
		bool inNonOrbitTabStrip = false)
		=> new SessionReconciliationSnapshot(
			SessionId: Guid.NewGuid(),
			Name: "test",
			SessionType: SessionType.RuneScape,
			State: state,
			InjectionState: InjectionState.NotReady,
			Placement: placement,
			IsMoveInProgress: moving,
			InSessionCollection: inCollection,
			InOrbitWorkspace: inOrbitWorkspace,
			InAnyTabStrip: inAnyTabStrip,
			InOrbitTabStrip: inOrbitTabStrip,
			InNonOrbitTabStrip: inNonOrbitTabStrip,
			TabOwners: new List<string>(),
			ProcessRunning: true,
			ProcessId: 1234,
			ExternalHandle: nint.Zero,
			Reason: null);
}
