using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Orbit.Logging;
using Orbit.Models;

namespace Orbit.Services;

public sealed class ShellSessionCloseService
{
	private readonly SessionManagerService sessionManager;
	private readonly SessionLifecycleCoordinatorService lifecycleCoordinator;
	private readonly SessionPlacementService placementService;
	private readonly SessionReconciliationService reconciliationService;
	private readonly ConsoleLogService consoleLog;

	public ShellSessionCloseService(
		SessionManagerService sessionManager,
		SessionLifecycleCoordinatorService lifecycleCoordinator,
		SessionPlacementService placementService,
		SessionReconciliationService reconciliationService,
		ConsoleLogService consoleLog)
	{
		this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
		this.lifecycleCoordinator = lifecycleCoordinator ?? throw new ArgumentNullException(nameof(lifecycleCoordinator));
		this.placementService = placementService ?? throw new ArgumentNullException(nameof(placementService));
		this.reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
		this.consoleLog = consoleLog ?? throw new ArgumentNullException(nameof(consoleLog));
	}

	public Task ShutdownTrackedProcessesAsync(bool forceKillOnTimeout = false, CancellationToken cancellationToken = default)
		=> sessionManager.ShutdownManagedProcessesAsync(forceKillOnTimeout, cancellationToken);

	public async Task CloseSessionAsync(
		SessionModel? session,
		bool skipConfirmation,
		bool forceKillOnTimeout,
		ObservableCollection<SessionModel> sessions,
		Func<IEnumerable<object>> captureOrbitItems,
		Func<SessionModel, Task<bool>> confirmCloseAsync,
		Func<SessionModel, bool> removeFromVisibleShell,
		Action<SessionModel> ensureVisible)
	{
		if (session == null)
		{
			return;
		}

		if (!lifecycleCoordinator.TryBeginClose(session))
		{
			return;
		}

		try
		{
			if (!skipConfirmation)
			{
				var confirmed = await confirmCloseAsync(session).ConfigureAwait(true);
				if (!confirmed)
				{
					return;
				}
			}

			placementService.SetPlacement(session, SessionPlacementKind.Closing);
			reconciliationService.LogDecision(
				"close-start",
				reconciliationService.Capture(session, captureOrbitItems(), "close-start"));

			var removeFromUiCollections = false;
			var removedFromTabShell = false;
			try
			{
				var pid = session.RSProcess?.Id;
				consoleLog.Append(
					$"[Orbit] Requesting shutdown for session '{session.Name}'{(pid.HasValue ? $" (PID {pid.Value})" : string.Empty)}.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				removedFromTabShell = removeFromVisibleShell(session);
				reconciliationService.LogDecision(
					"close-removed-visible-shell",
					reconciliationService.Capture(session, captureOrbitItems(), "close-visible-remove"),
					$"removedFromTabShell={removedFromTabShell}");

				await sessionManager.ShutdownSessionAsync(session, forceKillOnTimeout: forceKillOnTimeout).ConfigureAwait(true);
				removeFromUiCollections = true;
			}
			catch (Exception ex)
			{
				consoleLog.Append(
					$"[Orbit] Failed to shutdown session '{session.Name}': {ex.Message}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Error);

				removeFromUiCollections = !IsSessionProcessStillRunning(session);
				if (!removeFromUiCollections)
				{
					var recoveryState = session.InjectionState == InjectionState.Injected
						? SessionState.Injected
						: SessionState.ClientReady;
					session.UpdateState(recoveryState, clearError: false);
					reconciliationService.LogDecision(
						"close-recover-running-session",
						reconciliationService.Capture(session, captureOrbitItems(), "shutdown-failed"),
						ex.Message);
					consoleLog.Append(
						$"[Orbit] Session '{session.Name}' is still running after shutdown failure; keeping it in UI for manual recovery.",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Warning);
				}
			}
			finally
			{
				if (removeFromUiCollections)
				{
					reconciliationService.LogDecision(
						"close-final-remove",
						reconciliationService.Capture(session, captureOrbitItems(), "shutdown-complete"));

					if (sessions.Contains(session))
					{
						sessions.Remove(session);
					}

					placementService.Remove(session);

					if (!removedFromTabShell)
					{
						removeFromVisibleShell(session);
					}
				}
				else
				{
					if (placementService.GetPlacement(session) == SessionPlacementKind.Closing)
					{
						placementService.SetPlacement(session, SessionPlacementKind.Unknown, "close-aborted-or-failed");
					}

					ensureVisible(session);
				}
			}
		}
		finally
		{
			lifecycleCoordinator.CompleteClose(session);
		}
	}

	private static bool IsSessionProcessStillRunning(SessionModel session)
	{
		var process = session.RSProcess;
		if (process == null)
		{
			return false;
		}

		try
		{
			return !process.HasExited;
		}
		catch
		{
			return false;
		}
	}
}
