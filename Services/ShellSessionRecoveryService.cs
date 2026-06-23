using System;
using System.Collections.Generic;
using Orbit.Logging;
using Orbit.Models;

namespace Orbit.Services;

public sealed class ShellSessionRecoveryService
{
	private readonly SessionPlacementService sessionPlacementService;
	private readonly SessionReconciliationService sessionReconciliationService;
	private readonly SessionLifecycleCoordinatorService sessionLifecycleCoordinator;
	private readonly ConsoleLogService consoleLog;

	public ShellSessionRecoveryService(
		SessionPlacementService sessionPlacementService,
		SessionReconciliationService sessionReconciliationService,
		SessionLifecycleCoordinatorService sessionLifecycleCoordinator,
		ConsoleLogService consoleLog)
	{
		this.sessionPlacementService = sessionPlacementService ?? throw new ArgumentNullException(nameof(sessionPlacementService));
		this.sessionReconciliationService = sessionReconciliationService ?? throw new ArgumentNullException(nameof(sessionReconciliationService));
		this.sessionLifecycleCoordinator = sessionLifecycleCoordinator ?? throw new ArgumentNullException(nameof(sessionLifecycleCoordinator));
		this.consoleLog = consoleLog ?? throw new ArgumentNullException(nameof(consoleLog));
	}

	public void EnsureSessionRemainsVisible(
		SessionModel session,
		ICollection<SessionModel> sessions,
		ICollection<object> tabs,
		IEnumerable<object> orbitItems,
		Action<SessionModel> selectSession,
		Action<object> selectTab)
	{
		if (session == null || sessions == null || tabs == null || !sessions.Contains(session))
		{
			return;
		}

		if (sessionPlacementService.GetPlacement(session) == SessionPlacementKind.Closing)
		{
			return;
		}

		if (!sessionPlacementService.IsPlacedInHost(session))
		{
			sessionPlacementService.SetPlacement(session, SessionPlacementKind.MainTabs, "restore-visible-session");
			if (!tabs.Contains(session))
			{
				tabs.Add(session);
			}
		}

		selectSession(session);
		if (tabs.Contains(session))
		{
			selectTab(session);
		}
	}

	public void ValidateOrphanedSession(
		SessionModel session,
		bool isDisposed,
		IEnumerable<object> orbitItems,
		Action<SessionModel> ensureVisible)
	{
		try
		{
			if (isDisposed || session == null || sessionLifecycleCoordinator.IsCloseInProgress(session))
			{
				return;
			}

			// Placement-driven (Stage 2c): conflicts are now resolved by the ownership coordinator,
			// so this path only restores a session the authoritative model reports in no host.
			if (!sessionReconciliationService.ShouldRestoreOrphan(session))
			{
				return;
			}

			Console.WriteLine(
				$"[Orbit][Reconcile] orphan-restore session='{session.Name}' placement={sessionPlacementService.GetPlacement(session)}");
			consoleLog.Append(
				$"[Orbit] Session '{session.Name}' temporarily lost all UI references; restoring it to tabs instead of closing the process.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);

			ensureVisible(session);
		}
		finally
		{
			sessionLifecycleCoordinator.CompleteOrphanValidation(session);
		}
	}
}
