using System.Collections.Generic;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionTargetResolverServiceTests
{
	[Fact]
	public void ResolveHotReloadTarget_PrefersSharedTargetInSessionCollection()
	{
		var resolver = new SessionTargetResolverService();
		var shared = NewSession("Shared");
		var local = NewSession("Local");
		var selected = NewSession("Selected");
		var sessions = new List<SessionModel> { selected, local, shared };

		var target = resolver.ResolveHotReloadTarget(sessions, shared, local, selected, null);

		Assert.Same(shared, target);
	}

	[Fact]
	public void ResolveHotReloadTarget_UsesLocalTargetWhenSharedTargetIsStale()
	{
		var resolver = new SessionTargetResolverService();
		var staleShared = NewSession("Stale");
		var local = NewSession("Local");
		var selected = NewSession("Selected");
		var sessions = new List<SessionModel> { selected, local };

		var target = resolver.ResolveHotReloadTarget(sessions, staleShared, local, selected, null);

		Assert.Same(local, target);
	}

	[Fact]
	public void ResolveHotReloadTarget_UsesSelectedThenGlobalSelectedThenFirstSession()
	{
		var resolver = new SessionTargetResolverService();
		var first = NewSession("First");
		var selected = NewSession("Selected");
		var global = NewSession("Global");
		var sessions = new List<SessionModel> { first, selected, global };

		Assert.Same(selected, resolver.ResolveHotReloadTarget(sessions, null, null, selected, global));
		Assert.Same(global, resolver.ResolveHotReloadTarget(sessions, null, null, NewSession("Stale"), global));
		Assert.Same(first, resolver.ResolveHotReloadTarget(sessions, null, null, NewSession("Stale"), NewSession("Other Stale")));
	}

	private static SessionModel NewSession(string name)
	{
		return new SessionModel { Name = name };
	}
}
