using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

public sealed class ShellTabCollectionCoordinatorService
{
	private readonly ShellTabSelectionService tabSelectionService;
	private readonly SessionPlacementService sessionPlacementService;
	private readonly OrbitLayoutStateService orbitLayoutState;

	public ShellTabCollectionCoordinatorService(
		ShellTabSelectionService tabSelectionService,
		SessionPlacementService sessionPlacementService,
		OrbitLayoutStateService orbitLayoutState)
	{
		this.tabSelectionService = tabSelectionService ?? throw new ArgumentNullException(nameof(tabSelectionService));
		this.sessionPlacementService = sessionPlacementService ?? throw new ArgumentNullException(nameof(sessionPlacementService));
		this.orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));
	}

	public ShellTabCollectionChangeResult HandleCollectionChanged(
		IEnumerable<object?> tabs,
		object? selectedTab,
		NotifyCollectionChangedAction action,
		IEnumerable<object?>? oldItems,
		IEnumerable<object?>? newItems,
		bool isOrbitViewTearOffWindow,
		bool isMainWindowShell)
	{
		var selection = tabSelectionService.ResolveAfterCollectionChanged(
			tabs,
			selectedTab,
			action,
			oldItems,
			newItems);

		var orphanValidationCandidates = GetOrphanValidationCandidates(oldItems);

		// A drag-out removes the tab from this host before the matching add lands in the
		// destination host. Open a move grace for the gap so concurrent reconcile passes do
		// not mistake the in-flight session for an orphan (the cause of dedup/lost sessions).
		// The grace is closed by the landing add (ReconcileAddedSession) or, as a backstop,
		// by final orphan validation.
		if (action == NotifyCollectionChangedAction.Remove)
		{
			foreach (var session in orphanValidationCandidates)
			{
				sessionPlacementService.BeginExternalMove(session, "tab-removed-pending-rehome");
			}
		}

		ReconcileAddedItems(newItems, isOrbitViewTearOffWindow, isMainWindowShell);

		return new ShellTabCollectionChangeResult(
			selection.SelectedTab,
			selection.ClearSelectedSession,
			orphanValidationCandidates);
	}

	private IReadOnlyList<SessionModel> GetOrphanValidationCandidates(IEnumerable<object?>? oldItems)
	{
		if (oldItems == null)
		{
			return Array.Empty<SessionModel>();
		}

		return oldItems
			.OfType<SessionModel>()
			.Where(session =>
				!sessionPlacementService.IsMoveInProgress(session) &&
				sessionPlacementService.GetPlacement(session) is not SessionPlacementKind.OrbitWorkspace and not SessionPlacementKind.Closing)
			.ToList();
	}

	private void ReconcileAddedItems(
		IEnumerable<object?>? newItems,
		bool isOrbitViewTearOffWindow,
		bool isMainWindowShell)
	{
		if (newItems == null)
		{
			return;
		}

		foreach (var added in newItems.Where(item => item != null).Cast<object>())
		{
			switch (added)
			{
				case SessionModel session:
					ReconcileAddedSession(session, isOrbitViewTearOffWindow, isMainWindowShell);
					break;
				case ToolTabItem tool when !isOrbitViewTearOffWindow &&
					!string.Equals(tool.Key, ShellPresentationPolicyService.OrbitViewToolKey, StringComparison.Ordinal):
					orbitLayoutState.RemoveItem(tool);
					break;
			}
		}
	}

	private void ReconcileAddedSession(
		SessionModel session,
		bool isOrbitViewTearOffWindow,
		bool isMainWindowShell)
	{
		// The session has landed in a host; close any grace opened by its drag-out removal.
		sessionPlacementService.EndExternalMove(session, "tab-add-landed");

		if (isOrbitViewTearOffWindow)
		{
			sessionPlacementService.SetPlacement(session, SessionPlacementKind.OrbitWorkspace, "orbit-tearoff-tab-add");
			return;
		}

		sessionPlacementService.SetPlacement(
			session,
			isMainWindowShell ? SessionPlacementKind.MainTabs : SessionPlacementKind.TearOffWindow);
		orbitLayoutState.RemoveItem(session);
	}
}
