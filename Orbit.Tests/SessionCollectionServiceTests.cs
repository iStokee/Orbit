using System.Diagnostics;
using Orbit.Models;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionCollectionServiceTests
{
	[Fact]
	public void AddingSession_WithStaleProcessHandle_DoesNotThrow()
	{
		// Regression: a session carrying an unstarted/stale Process handle (the state a ghost or
		// duplicated session ends up in) throws on Process.HasExited. ValidateSessionIdentity runs
		// on every collection change and previously let that exception escape, crashing the app
		// when a new session was added.
		var ghost = new SessionModel { Name = "Ghost", RSProcess = new Process() };
		var added = new SessionModel { Name = "Fresh" };

		try
		{
			SessionCollectionService.Instance.Sessions.Add(ghost);

			var exception = Record.Exception(() => SessionCollectionService.Instance.Sessions.Add(added));

			Assert.Null(exception);
		}
		finally
		{
			SessionCollectionService.Instance.Sessions.Remove(added);
			SessionCollectionService.Instance.Sessions.Remove(ghost);
			ghost.RSProcess?.Dispose();
		}
	}
}
