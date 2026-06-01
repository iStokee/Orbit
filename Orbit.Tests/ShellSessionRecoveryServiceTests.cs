using System.Collections.ObjectModel;
using System.Linq;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class ShellSessionRecoveryServiceTests
{
	[Fact]
	public void EnsureSessionRemainsVisible_RestoresSessionWithoutUiReferenceToMainTabs()
	{
		var fixture = CreateFixture();
		var session = new SessionModel { Name = "Recovered" };
		var tabs = new ObservableCollection<object>();
		SessionModel? selectedSession = null;
		object? selectedTab = null;

		try
		{
			SessionCollectionService.Instance.Sessions.Add(session);

			fixture.Service.EnsureSessionRemainsVisible(
				session,
				SessionCollectionService.Instance.Sessions,
				tabs,
				Enumerable.Empty<object>(),
				selected => selectedSession = selected,
				selected => selectedTab = selected);

			Assert.Same(session, tabs.Single());
			Assert.Equal(SessionPlacementKind.MainTabs, fixture.Placement.GetPlacement(session));
			Assert.Same(session, selectedSession);
			Assert.Same(session, selectedTab);
		}
		finally
		{
			SessionCollectionService.Instance.Sessions.Remove(session);
		}
	}

	[Fact]
	public void EnsureSessionRemainsVisible_DoesNotRestoreClosingSession()
	{
		var fixture = CreateFixture();
		var session = new SessionModel { Name = "Closing" };
		var tabs = new ObservableCollection<object>();
		var selected = false;

		try
		{
			SessionCollectionService.Instance.Sessions.Add(session);
			fixture.Placement.SetPlacement(session, SessionPlacementKind.Closing);

			fixture.Service.EnsureSessionRemainsVisible(
				session,
				SessionCollectionService.Instance.Sessions,
				tabs,
				Enumerable.Empty<object>(),
				_ => selected = true,
				_ => selected = true);

			Assert.Empty(tabs);
			Assert.False(selected);
			Assert.Equal(SessionPlacementKind.Closing, fixture.Placement.GetPlacement(session));
		}
		finally
		{
			SessionCollectionService.Instance.Sessions.Remove(session);
		}
	}

	private static TestFixture CreateFixture()
	{
		var placement = new SessionPlacementService();
		var lifecycle = new SessionLifecycleCoordinatorService();
		var reconciliation = new SessionReconciliationService(
			SessionCollectionService.Instance,
			placement,
			new TearOffHostRegistry());
		var service = new ShellSessionRecoveryService(
			placement,
			reconciliation,
			lifecycle,
			new ConsoleLogService());

		return new TestFixture(service, placement);
	}

	private sealed record TestFixture(
		ShellSessionRecoveryService Service,
		SessionPlacementService Placement);
}
