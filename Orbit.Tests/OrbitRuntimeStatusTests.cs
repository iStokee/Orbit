using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class OrbitRuntimeStatusTests
{
	[Fact]
	public void IsScriptLoaded_MatchesDashboardScriptRowsById()
	{
		var status = new OrbitRuntimeStatus(
			Ok: true,
			ProcessId: 123,
			RuntimeRunning: true,
			ScriptId: "alpha",
			ScriptsInfo: string.Join('\n',
				"DASHBOARD_V1",
				"COUNT\t1",
				"SCRIPT\talpha\tRunning\tAlpha\t1.0.0.0\tC:\\Scripts\\Alpha.dll\t2026-01-01T00:00:00.0000000Z\t2026-01-01T00:00:00.0000000Z\t\tAlive"));

		Assert.True(status.IsScriptLoaded("alpha"));
		Assert.False(status.IsScriptLoaded("beta"));
	}

	[Fact]
	public void IsScriptLoaded_DoesNotMatchPartialScriptIds()
	{
		var status = new OrbitRuntimeStatus(
			Ok: true,
			ProcessId: 123,
			RuntimeRunning: true,
			ScriptId: "alpha2",
			ScriptsInfo: "SCRIPT\talpha2\tRunning\tAlpha2\t1.0.0.0\tC:\\Scripts\\Alpha2.dll\tloaded\tupdated\t\tAlive");

		Assert.False(status.IsScriptLoaded("alpha"));
		Assert.True(status.IsScriptLoaded("ALPHA2"));
	}

	[Fact]
	public void GetScriptPath_ReturnsPathFromDashboardRow()
	{
		var status = new OrbitRuntimeStatus(
			Ok: true,
			ProcessId: 123,
			RuntimeRunning: true,
			ScriptId: "alpha",
			ScriptsInfo: "SCRIPT\talpha\tRunning\tAlpha\t1.0.0.0\tC:\\Scripts\\Alpha.dll\tloaded\tupdated\t\tAlive");

		Assert.Equal("C:\\Scripts\\Alpha.dll", status.GetScriptPath("alpha"));
	}

	[Fact]
	public void GetScriptPath_ReturnsNullForMalformedOrMissingRows()
	{
		var status = new OrbitRuntimeStatus(
			Ok: true,
			ProcessId: 123,
			RuntimeRunning: true,
			ScriptId: null,
			ScriptsInfo: string.Join('\n',
				"SCRIPT\talpha\tRunning",
				"SCRIPT\tbeta\tRunning\tBeta\t1.0.0.0\tC:\\Scripts\\Beta.dll\tloaded\tupdated\t\tAlive"));

		Assert.Null(status.GetScriptPath("alpha"));
		Assert.Null(status.GetScriptPath("gamma"));
		Assert.Equal("C:\\Scripts\\Beta.dll", status.GetScriptPath("beta"));
	}
}
