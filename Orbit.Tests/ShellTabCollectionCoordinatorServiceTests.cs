using System.Collections.Specialized;
using System.Linq;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellTabCollectionCoordinatorServiceTests
{
	[Fact]
	public void HandleCollectionChanged_MarksAddedSessionAsMainTabsForMainShell()
	{
		var fixture = CreateFixture();
		var session = new SessionModel();

		fixture.Service.HandleCollectionChanged(
			new object[] { session },
			selectedTab: null,
			NotifyCollectionChangedAction.Add,
			oldItems: null,
			newItems: new object[] { session },
			isOrbitViewTearOffWindow: false,
			isMainWindowShell: true);

		Assert.Equal(SessionPlacementKind.MainTabs, fixture.Placement.GetPlacement(session));
	}

	[Fact]
	public void HandleCollectionChanged_MarksAddedSessionAsTearOffForDetachedShell()
	{
		var fixture = CreateFixture();
		var session = new SessionModel();

		fixture.Service.HandleCollectionChanged(
			new object[] { session },
			selectedTab: null,
			NotifyCollectionChangedAction.Add,
			oldItems: null,
			newItems: new object[] { session },
			isOrbitViewTearOffWindow: false,
			isMainWindowShell: false);

		Assert.Equal(SessionPlacementKind.TearOffWindow, fixture.Placement.GetPlacement(session));
	}

	[Fact]
	public void HandleCollectionChanged_MarksAddedSessionAsOrbitWorkspaceForOrbitTearOff()
	{
		var fixture = CreateFixture();
		var session = new SessionModel();

		fixture.Service.HandleCollectionChanged(
			new object[] { session },
			selectedTab: null,
			NotifyCollectionChangedAction.Add,
			oldItems: null,
			newItems: new object[] { session },
			isOrbitViewTearOffWindow: true,
			isMainWindowShell: false);

		Assert.Equal(SessionPlacementKind.OrbitWorkspace, fixture.Placement.GetPlacement(session));
	}

	[Fact]
	public void HandleCollectionChanged_ReturnsRemovedMainTabSessionForOrphanValidation()
	{
		var fixture = CreateFixture();
		var session = new SessionModel();
		fixture.Placement.SetPlacement(session, SessionPlacementKind.MainTabs);

		var result = fixture.Service.HandleCollectionChanged(
			new object[0],
			selectedTab: session,
			NotifyCollectionChangedAction.Remove,
			oldItems: new object[] { session },
			newItems: null,
			isOrbitViewTearOffWindow: false,
			isMainWindowShell: true);

		Assert.Same(session, result.SessionsNeedingOrphanValidation.Single());
		Assert.Null(result.SelectedTab);
		Assert.True(result.ClearSelectedSession);
	}

	[Theory]
	[InlineData(SessionPlacementKind.OrbitWorkspace)]
	[InlineData(SessionPlacementKind.Closing)]
	public void HandleCollectionChanged_DoesNotValidateExpectedRemovals(SessionPlacementKind placement)
	{
		var fixture = CreateFixture();
		var session = new SessionModel();
		fixture.Placement.SetPlacement(session, placement);

		var result = fixture.Service.HandleCollectionChanged(
			new object[0],
			selectedTab: null,
			NotifyCollectionChangedAction.Remove,
			oldItems: new object[] { session },
			newItems: null,
			isOrbitViewTearOffWindow: false,
			isMainWindowShell: true);

		Assert.Empty(result.SessionsNeedingOrphanValidation);
	}

	private static TestFixture CreateFixture()
	{
		var placement = new SessionPlacementService();
		var reconciliation = new SessionReconciliationService(
			SessionCollectionService.Instance,
			placement,
			new TearOffHostRegistry());
		var layout = new OrbitLayoutStateService(
			SessionCollectionService.Instance,
			placement,
			reconciliation);
		var service = new ShellTabCollectionCoordinatorService(
			new ShellTabSelectionService(),
			placement,
			layout);

		return new TestFixture(service, placement);
	}

	private sealed record TestFixture(
		ShellTabCollectionCoordinatorService Service,
		SessionPlacementService Placement);
}
