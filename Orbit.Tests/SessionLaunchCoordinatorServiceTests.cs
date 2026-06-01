using System;
using System.Threading.Tasks;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class SessionLaunchCoordinatorServiceTests
{
	[Theory]
	[InlineData(null, "OrbitView")]
	[InlineData("", "OrbitView")]
	[InlineData("   ", "OrbitView")]
	[InlineData("SessionsTabbed", "IndividualTabs")]
	[InlineData("sessionstabbed", "IndividualTabs")]
	[InlineData("IndividualTabs", "IndividualTabs")]
	[InlineData("OrbitView", "OrbitView")]
	public void NormalizeSessionLaunchBehavior_HandlesDefaultsAndLegacyValues(string? value, string expected)
	{
		Assert.Equal(expected, SessionLaunchCoordinatorService.NormalizeSessionLaunchBehavior(value));
	}

	[Fact]
	public async Task LaunchSelectedBatchAsync_SerializesOrbitViewLaunchesAndStopsOnFailure()
	{
		var launchCalls = 0;
		var coordinator = new SessionLaunchCoordinatorService(_ => Task.CompletedTask);

		var launched = await coordinator.LaunchSelectedBatchAsync(
			batchCount: 3,
			fullWaitForDock: false,
			launchDelaySeconds: 5,
			launchBehavior: "OrbitView",
			addSingleSessionAsync: () =>
			{
				launchCalls++;
				return Task.FromResult(launchCalls < 2);
			});

		Assert.Equal(1, launched);
		Assert.Equal(2, launchCalls);
		Assert.False(coordinator.IsBatchLaunchInProgress);
	}

	[Fact]
	public async Task LaunchSelectedBatchAsync_UsesDelayForParallelTabLaunches()
	{
		var launchCalls = 0;
		var delayCalls = 0;
		var coordinator = new SessionLaunchCoordinatorService(_ =>
		{
			delayCalls++;
			return Task.CompletedTask;
		});

		var launched = await coordinator.LaunchSelectedBatchAsync(
			batchCount: 3,
			fullWaitForDock: false,
			launchDelaySeconds: 1,
			launchBehavior: "IndividualTabs",
			addSingleSessionAsync: () =>
			{
				launchCalls++;
				return Task.FromResult(true);
			});

		Assert.Equal(3, launched);
		Assert.Equal(3, launchCalls);
		Assert.Equal(2, delayCalls);
	}

	[Fact]
	public async Task LaunchSelectedBatchAsync_IgnoresReentrantBatch()
	{
		var coordinator = new SessionLaunchCoordinatorService(_ => Task.CompletedTask);
		var blocker = new TaskCompletionSource<bool>();
		var reentrantResult = -1;

		var first = coordinator.LaunchSelectedBatchAsync(
			batchCount: 1,
			fullWaitForDock: true,
			launchDelaySeconds: 5,
			launchBehavior: "OrbitView",
			addSingleSessionAsync: async () =>
			{
				reentrantResult = await coordinator.LaunchSelectedBatchAsync(
					batchCount: 1,
					fullWaitForDock: true,
					launchDelaySeconds: 5,
					launchBehavior: "OrbitView",
					addSingleSessionAsync: () => Task.FromResult(true));
				blocker.SetResult(true);
				return true;
			});

		await blocker.Task;
		var firstResult = await first;

		Assert.Equal(1, firstResult);
		Assert.Equal(0, reentrantResult);
	}
}
