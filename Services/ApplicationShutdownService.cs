using System;
using System.Linq;
using System.Threading.Tasks;
using Orbit.Models;
using Orbit.Plugins;

namespace Orbit.Services;

/// <summary>One awaited cleanup operation for normal exit and update installation.</summary>
public sealed class ApplicationShutdownService
{
	private readonly Func<Task> _cleanup;
	private Task? _shutdown;
	private readonly object _sync = new();
	public bool IsShuttingDown => _shutdown != null && !_shutdown.IsFaulted && !_shutdown.IsCanceled;

	public ApplicationShutdownService(SessionCollectionService sessions, SessionManagerService manager,
		SessionPlacementService placement, OrbitLayoutStateService layout, PluginManager plugins, Action? preparePresentation = null)
		: this(async () =>
		{
			var snapshot = sessions.Sessions.ToArray();
			foreach (var session in snapshot) session.UpdateState(SessionState.ShuttingDown, clearError: false);
			foreach (var session in snapshot)
			{
				try
				{
					await manager.ShutdownSessionAsync(session).ConfigureAwait(true);
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Orbit] Client left running after graceful shutdown failed: {ex.Message}");
					manager.DetachPresentation(session);
				}
				layout.RemoveItem(session);
				placement.Remove(session);
				sessions.Sessions.Remove(session);
			}
			preparePresentation?.Invoke();
			await manager.ShutdownManagedProcessesAsync().ConfigureAwait(true);
			await plugins.ShutdownAsync().ConfigureAwait(true);
		}) { }

	internal ApplicationShutdownService(Func<Task> cleanup) => _cleanup = cleanup;

	public Task PrepareAsync()
	{
		lock (_sync)
		{
			if (_shutdown != null && !_shutdown.IsFaulted && !_shutdown.IsCanceled) return _shutdown;
			var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			_shutdown = completion.Task;
			_ = RunAsync(completion);
			return _shutdown;
		}
	}

	private async Task RunAsync(TaskCompletionSource completion)
	{
		try { await _cleanup().ConfigureAwait(true); completion.SetResult(); }
		catch (Exception ex) { completion.SetException(ex); }
	}
}
