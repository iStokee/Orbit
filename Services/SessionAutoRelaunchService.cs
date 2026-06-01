using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Orbit.Logging;
using Orbit.Models;

namespace Orbit.Services;

public sealed class SessionAutoRelaunchService
{
	private readonly SessionLifecycleCoordinatorService lifecycleCoordinator;
	private readonly ConsoleLogService consoleLog;
	private bool checkRunning;

	public SessionAutoRelaunchService(
		SessionLifecycleCoordinatorService lifecycleCoordinator,
		ConsoleLogService consoleLog)
	{
		this.lifecycleCoordinator = lifecycleCoordinator ?? throw new ArgumentNullException(nameof(lifecycleCoordinator));
		this.consoleLog = consoleLog ?? throw new ArgumentNullException(nameof(consoleLog));
	}

	public async Task CheckAndRelaunchAsync(
		IEnumerable<SessionModel?> sessions,
		bool isShuttingDown,
		bool autoRelaunchEnabled,
		Func<SessionModel, Task> closeSessionAsync,
		Func<string?, Task<bool>> addSessionAsync)
	{
		if (isShuttingDown || checkRunning || !autoRelaunchEnabled)
		{
			return;
		}

		checkRunning = true;
		try
		{
			foreach (var session in sessions.Where(s => s != null).Cast<SessionModel>().ToList())
			{
				if (!IsUnexpectedExit(session))
				{
					continue;
				}

				await TryAutoRelaunchSessionAsync(session, isShuttingDown, autoRelaunchEnabled, closeSessionAsync, addSessionAsync)
					.ConfigureAwait(true);
			}
		}
		finally
		{
			checkRunning = false;
		}
	}

	public bool IsUnexpectedExit(SessionModel session)
	{
		return lifecycleCoordinator.IsUnexpectedExit(session, HasSessionProcessExited);
	}

	private async Task TryAutoRelaunchSessionAsync(
		SessionModel session,
		bool isShuttingDown,
		bool autoRelaunchEnabled,
		Func<SessionModel, Task> closeSessionAsync,
		Func<string?, Task<bool>> addSessionAsync)
	{
		if (!lifecycleCoordinator.TryBeginRelaunch(session))
		{
			return;
		}

		var originalName = session.Name;
		try
		{
			consoleLog.Append(
				$"[Orbit] Session '{session.Name}' exited unexpectedly. Auto-relaunching.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);

			await closeSessionAsync(session).ConfigureAwait(true);

			if (isShuttingDown || !autoRelaunchEnabled)
			{
				return;
			}

			var relaunched = await addSessionAsync(originalName).ConfigureAwait(true);
			if (!relaunched)
			{
				consoleLog.Append(
					$"[Orbit] Auto-relaunch failed for session '{originalName}'.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
			}
		}
		catch (Exception ex)
		{
			consoleLog.Append(
				$"[Orbit] Auto-relaunch error for session '{originalName}': {ex.Message}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Error);
		}
		finally
		{
			lifecycleCoordinator.CompleteRelaunch(session);
		}
	}

	private static bool HasSessionProcessExited(SessionModel session)
	{
		var process = session.RSProcess;
		if (process == null)
		{
			return false;
		}

		try
		{
			return process.HasExited;
		}
		catch
		{
			return false;
		}
	}
}
