using System;
using System.Collections.Generic;
using Orbit.Models;

namespace Orbit.Services;

public sealed class SessionLifecycleCoordinatorService
{
	private readonly object sync = new();
	private readonly HashSet<Guid> closingSessionIds = new();
	private readonly HashSet<Guid> pendingOrphanValidationSessionIds = new();
	private readonly HashSet<Guid> relaunchingSessionIds = new();

	public bool IsCloseInProgress(SessionModel? session)
	{
		if (session == null)
		{
			return false;
		}

		lock (sync)
		{
			return closingSessionIds.Contains(session.Id);
		}
	}

	public bool TryBeginClose(SessionModel? session)
	{
		if (session == null)
		{
			return false;
		}

		lock (sync)
		{
			return closingSessionIds.Add(session.Id);
		}
	}

	public void CompleteClose(SessionModel? session)
	{
		if (session == null)
		{
			return;
		}

		lock (sync)
		{
			closingSessionIds.Remove(session.Id);
		}
	}

	public bool TryBeginOrphanValidation(SessionModel? session)
	{
		if (session == null)
		{
			return false;
		}

		lock (sync)
		{
			return pendingOrphanValidationSessionIds.Add(session.Id);
		}
	}

	public void CompleteOrphanValidation(SessionModel? session)
	{
		if (session == null)
		{
			return;
		}

		lock (sync)
		{
			pendingOrphanValidationSessionIds.Remove(session.Id);
		}
	}

	public void ClearPendingOrphanValidations()
	{
		lock (sync)
		{
			pendingOrphanValidationSessionIds.Clear();
		}
	}

	public bool TryBeginRelaunch(SessionModel? session)
	{
		if (session == null)
		{
			return false;
		}

		lock (sync)
		{
			return relaunchingSessionIds.Add(session.Id);
		}
	}

	public void CompleteRelaunch(SessionModel? session)
	{
		if (session == null)
		{
			return;
		}

		lock (sync)
		{
			relaunchingSessionIds.Remove(session.Id);
		}
	}

	public bool ShouldConfirmClose(SessionModel? session)
	{
		if (session == null || !session.IsRuneScapeClient)
		{
			return false;
		}

		return session.State == SessionState.ClientReady ||
			   session.State == SessionState.Injecting ||
			   session.State == SessionState.Injected ||
			   session.InjectionState == InjectionState.Injected;
	}

	public bool IsUnexpectedExit(SessionModel? session, Func<SessionModel, bool> hasExited)
	{
		if (session == null)
		{
			return false;
		}

		if (hasExited == null)
		{
			throw new ArgumentNullException(nameof(hasExited));
		}

		if (!session.IsRuneScapeClient)
		{
			return false;
		}

		if (session.State is SessionState.Closed or SessionState.ShuttingDown)
		{
			return false;
		}

		if (IsCloseInProgress(session))
		{
			return false;
		}

		return hasExited(session);
	}
}
