using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionOwnershipCoordinatorServiceTests
{
	[Theory]
	[InlineData(SessionPlacementKind.OrbitWorkspace, SessionTabOwnerScope.NonOrbitOnly)]
	[InlineData(SessionPlacementKind.MainTabs, SessionTabOwnerScope.OrbitOnly)]
	[InlineData(SessionPlacementKind.TearOffWindow, SessionTabOwnerScope.OrbitOnly)]
	public void ResolveEvictionScope_EvictsTheNonAuthoritativeHost(
		SessionPlacementKind placement,
		SessionTabOwnerScope expectedScope)
	{
		// The session is kept in the host matching its placement; the opposite scope is evicted.
		Assert.Equal(expectedScope, SessionOwnershipCoordinatorService.ResolveEvictionScope(placement));
	}

	[Theory]
	[InlineData(SessionPlacementKind.Unknown)]
	[InlineData(SessionPlacementKind.Closing)]
	public void ResolveEvictionScope_NonConcreteHost_DoesNothing(SessionPlacementKind placement)
	{
		// Unknown/Closing are not a concrete host — there is nothing to enforce.
		Assert.Null(SessionOwnershipCoordinatorService.ResolveEvictionScope(placement));
	}
}
