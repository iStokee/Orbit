using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orbit.Models;

namespace Orbit.Services;

public sealed class SessionLaunchCoordinatorService
{
	private readonly Func<TimeSpan, Task> _delayAsync;
	private bool _isBatchLaunchInProgress;

	public SessionLaunchCoordinatorService()
		: this(delay => Task.Delay(delay))
	{
	}

	internal SessionLaunchCoordinatorService(Func<TimeSpan, Task> delayAsync)
	{
		_delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
	}

	public bool IsBatchLaunchInProgress => _isBatchLaunchInProgress;

	public async Task AddSessionAsync(Func<Task<bool>> addSingleSessionAsync, Action? invalidateCommands = null)
	{
		if (addSingleSessionAsync == null)
		{
			throw new ArgumentNullException(nameof(addSingleSessionAsync));
		}

		if (TryGetLauncherBatchLaunchCount(out var batchCount))
		{
			await LaunchSelectedBatchAsync(
				batchCount,
				Settings.Default.LauncherBatchWaitForDockBeforeNext,
				Settings.Default.LauncherBatchLaunchDelaySeconds,
				Settings.Default.SessionLaunchBehavior,
				addSingleSessionAsync,
				LauncherAccountStore.BeginSelectedLaunchBatch,
				invalidateCommands).ConfigureAwait(true);
			return;
		}

		await addSingleSessionAsync().ConfigureAwait(true);
	}

	internal async Task<int> LaunchSelectedBatchAsync(
		int batchCount,
		bool fullWaitForDock,
		double launchDelaySeconds,
		string? launchBehavior,
		Func<Task<bool>> addSingleSessionAsync,
		Action? beginBatch = null,
		Action? invalidateCommands = null)
	{
		if (batchCount <= 0)
		{
			return 0;
		}

		if (_isBatchLaunchInProgress)
		{
			Console.WriteLine("[Orbit][Launcher] Batch launch request ignored because a batch is already in progress.");
			return 0;
		}

		_isBatchLaunchInProgress = true;
		try
		{
			Console.WriteLine($"[Orbit][Launcher] Batch launch requested for {batchCount} selected account(s).");
			beginBatch?.Invoke();

			var normalizedDelaySeconds = Math.Clamp(launchDelaySeconds, 5, 30);
			var normalizedLaunchBehavior = NormalizeSessionLaunchBehavior(launchBehavior);
			var serializeBatchLaunch = fullWaitForDock || string.Equals(normalizedLaunchBehavior, "OrbitView", StringComparison.Ordinal);

			if (serializeBatchLaunch)
			{
				if (!fullWaitForDock && string.Equals(normalizedLaunchBehavior, "OrbitView", StringComparison.Ordinal))
				{
					Console.WriteLine("[Orbit][Launcher] Serializing batch launch because Orbit View workspace updates are not safe under concurrent session creation.");
				}

				return await LaunchSerialAsync(batchCount, addSingleSessionAsync).ConfigureAwait(true);
			}

			return await LaunchWithDelayAsync(batchCount, TimeSpan.FromSeconds(normalizedDelaySeconds), addSingleSessionAsync).ConfigureAwait(true);
		}
		finally
		{
			_isBatchLaunchInProgress = false;
			invalidateCommands?.Invoke();
		}
	}

	internal static bool TryGetLauncherBatchLaunchCount(out int batchCount)
	{
		batchCount = 0;
		if (!string.Equals(Settings.Default.ClientLaunchMode, "Launcher", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		var selected = LauncherAccountStore.LoadSelected();
		if (selected.Count <= 1)
		{
			return false;
		}

		batchCount = selected.Count;
		return true;
	}

	private async Task<int> LaunchSerialAsync(int batchCount, Func<Task<bool>> addSingleSessionAsync)
	{
		var launched = 0;
		for (var i = 0; i < batchCount; i++)
		{
			var docked = await addSingleSessionAsync().ConfigureAwait(true);
			if (!docked)
			{
				Console.WriteLine($"[Orbit][Launcher] Batch launch stopped after slot {i + 1}: session did not dock successfully.");
				break;
			}

			launched++;
		}

		return launched;
	}

	private async Task<int> LaunchWithDelayAsync(int batchCount, TimeSpan launchDelay, Func<Task<bool>> addSingleSessionAsync)
	{
		var launches = new List<Task<bool>>(batchCount);
		for (var i = 0; i < batchCount; i++)
		{
			launches.Add(addSingleSessionAsync());
			if (i < batchCount - 1)
			{
				await _delayAsync(launchDelay).ConfigureAwait(true);
			}
		}

		var results = await Task.WhenAll(launches).ConfigureAwait(true);
		var launched = 0;
		foreach (var result in results)
		{
			if (result)
			{
				launched++;
			}
		}

		return launched;
	}

	internal static string NormalizeSessionLaunchBehavior(string? launchBehavior)
	{
		if (string.IsNullOrWhiteSpace(launchBehavior))
		{
			return "OrbitView";
		}

		if (string.Equals(launchBehavior, "SessionsTabbed", StringComparison.OrdinalIgnoreCase))
		{
			return "IndividualTabs";
		}

		return launchBehavior;
	}
}
