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
