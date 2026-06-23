using System.Collections.Generic;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

/// <summary>
/// Characterization tests pinning the authoritative session-placement model before the
/// Cluster 1 refactor wires the Dragablz drag/tear-off path into it. These document
/// current behavior; they are not aspirational.
/// </summary>
public sealed class SessionPlacementServiceTests
{
	[Fact]
	public void UnknownSession_HasUnknownPlacement()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Fresh" };

		Assert.Equal(SessionPlacementKind.Unknown, placement.GetPlacement(session));
		Assert.False(placement.IsMoveInProgress(session));
	}

	[Fact]
	public void SetPlacement_UpdatesValue_AndRaisesChangeWithPreviousAndReason()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "S" };
		var events = new List<SessionPlacementChangedEventArgs>();
		placement.PlacementChanged += (_, e) => events.Add(e);

		placement.SetPlacement(session, SessionPlacementKind.MainTabs, "startup");

		Assert.Equal(SessionPlacementKind.MainTabs, placement.GetPlacement(session));
		var change = Assert.Single(events);
		Assert.Equal(SessionPlacementKind.Unknown, change.Previous);
		Assert.Equal(SessionPlacementKind.MainTabs, change.Current);
		Assert.Equal("startup", change.Reason);
	}

	[Fact]
	public void SetPlacement_ToSameValue_IsIdempotent_NoEvent()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "S" };
		placement.SetPlacement(session, SessionPlacementKind.MainTabs);

		var events = 0;
		placement.PlacementChanged += (_, _) => events++;
		placement.SetPlacement(session, SessionPlacementKind.MainTabs);

		Assert.Equal(0, events);
		Assert.Equal(SessionPlacementKind.MainTabs, placement.GetPlacement(session));
	}

	[Fact]
	public void BeginMove_MarksMoveInProgress_AndSetsTargetImmediately()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Moving" };
		placement.SetPlacement(session, SessionPlacementKind.MainTabs);

		using (placement.BeginMove(session, SessionPlacementKind.OrbitWorkspace))
		{
			Assert.True(placement.IsMoveInProgress(session));
			// Target placement is applied at move-start, not at completion.
			Assert.Equal(SessionPlacementKind.OrbitWorkspace, placement.GetPlacement(session));
		}

		// Completing the move clears the in-progress flag but leaves placement at the target.
		Assert.False(placement.IsMoveInProgress(session));
		Assert.Equal(SessionPlacementKind.OrbitWorkspace, placement.GetPlacement(session));
	}

	[Fact]
	public void Remove_ResetsToUnknown_AndRaisesRemovalEvent()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Gone" };
		placement.SetPlacement(session, SessionPlacementKind.TearOffWindow);

		SessionPlacementChangedEventArgs? last = null;
		placement.PlacementChanged += (_, e) => last = e;

		placement.Remove(session);

		Assert.Equal(SessionPlacementKind.Unknown, placement.GetPlacement(session));
		Assert.False(placement.IsMoveInProgress(session));
		Assert.NotNull(last);
		Assert.Equal(SessionPlacementKind.TearOffWindow, last!.Previous);
		Assert.Equal(SessionPlacementKind.Unknown, last.Current);
	}

	[Fact]
	public void Remove_UnknownSession_DoesNotRaiseEvent()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "NeverPlaced" };
		var events = 0;
		placement.PlacementChanged += (_, _) => events++;

		placement.Remove(session);

		Assert.Equal(0, events);
	}

	[Fact]
	public void BeginExternalMove_MarksMoving_WithoutChangingPlacement()
	{
		// A drag-out opens a grace but must NOT alter the recorded placement: the destination
		// host is unknown until the matching add lands.
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Dragging" };
		placement.SetPlacement(session, SessionPlacementKind.MainTabs);
		var placementEvents = 0;
		placement.PlacementChanged += (_, _) => placementEvents++;

		placement.BeginExternalMove(session, "drag-out");

		Assert.True(placement.IsMoveInProgress(session));
		Assert.Equal(SessionPlacementKind.MainTabs, placement.GetPlacement(session));
		Assert.Equal(0, placementEvents);
	}

	[Fact]
	public void EndExternalMove_ClearsGrace_AndIsIdempotent()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Landed" };
		placement.BeginExternalMove(session);

		placement.EndExternalMove(session);
		placement.EndExternalMove(session); // second call must be a harmless no-op

		Assert.False(placement.IsMoveInProgress(session));
	}

	[Fact]
	public void BeginExternalMove_IsIdempotent()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Twice" };

		placement.BeginExternalMove(session);
		placement.BeginExternalMove(session);
		placement.EndExternalMove(session); // single close balances repeated opens

		Assert.False(placement.IsMoveInProgress(session));
	}

	// --- Stage 2 ownership oracle -----------------------------------------------------------

	[Theory]
	[InlineData(SessionPlacementKind.Unknown, false, false, false)]
	[InlineData(SessionPlacementKind.MainTabs, true, false, true)]
	[InlineData(SessionPlacementKind.OrbitWorkspace, true, true, false)]
	[InlineData(SessionPlacementKind.TearOffWindow, true, false, true)]
	[InlineData(SessionPlacementKind.Closing, false, false, false)]
	public void OwnershipOracle_MatchesPlacement(
		SessionPlacementKind placementKind,
		bool placedInHost,
		bool inOrbitWorkspace,
		bool inNonOrbitHost)
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = placementKind.ToString() };
		placement.SetPlacement(session, placementKind);

		Assert.Equal(placedInHost, placement.IsPlacedInHost(session));
		Assert.Equal(inOrbitWorkspace, placement.IsInOrbitWorkspace(session));
		Assert.Equal(inNonOrbitHost, placement.IsInNonOrbitHost(session));
	}

	[Fact]
	public void OwnershipOracle_UnknownSession_IsNowhere()
	{
		var placement = new SessionPlacementService();
		var session = new SessionModel { Name = "Untracked" };

		Assert.False(placement.IsPlacedInHost(session));
		Assert.False(placement.IsInOrbitWorkspace(session));
		Assert.False(placement.IsInNonOrbitHost(session));
	}

	// --- Stage 2e: tool placement -----------------------------------------------------------

	[Theory]
	[InlineData(SessionPlacementKind.OrbitWorkspace, true, false)]
	[InlineData(SessionPlacementKind.MainTabs, false, true)]
	[InlineData(SessionPlacementKind.TearOffWindow, false, true)]
	[InlineData(SessionPlacementKind.Unknown, false, false)]
	public void ToolOwnershipOracle_MatchesPlacement(
		SessionPlacementKind placementKind,
		bool inOrbitWorkspace,
		bool inNonOrbitHost)
	{
		var placement = new SessionPlacementService();
		var tool = new ToolTabItem("tool-key", "Tool", null!);
		placement.SetPlacement(tool, placementKind);

		Assert.Equal(inOrbitWorkspace, placement.IsInOrbitWorkspace(tool));
		Assert.Equal(inNonOrbitHost, placement.IsInNonOrbitHost(tool));
	}

	[Fact]
	public void ToolPlacement_IsKeyedByToolKey_AndRemovable()
	{
		var placement = new SessionPlacementService();
		var tool = new ToolTabItem("shared-key", "Tool", null!);
		var sameKey = new ToolTabItem("shared-key", "Renamed", null!);

		placement.SetPlacement(tool, SessionPlacementKind.OrbitWorkspace);
		Assert.Equal(SessionPlacementKind.OrbitWorkspace, placement.GetPlacement(sameKey)); // keyed by Key

		placement.Remove(tool);
		Assert.Equal(SessionPlacementKind.Unknown, placement.GetPlacement(sameKey));
	}
}
