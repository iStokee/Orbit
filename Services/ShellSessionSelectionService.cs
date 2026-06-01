using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

public sealed class ShellSessionSelectionService
{
	public SessionModel? ResolveHotReloadTargetAfterSelectionChanged(
		IEnumerable<SessionModel?> sessions,
		SessionModel? selectedSession,
		SessionModel? currentHotReloadTarget)
	{
		var snapshot = sessions.ToList();
		if (selectedSession != null && (currentHotReloadTarget == null || !snapshot.Contains(currentHotReloadTarget)))
		{
			return selectedSession;
		}

		if (currentHotReloadTarget != null && !snapshot.Contains(currentHotReloadTarget))
		{
			return snapshot.FirstOrDefault();
		}

		return currentHotReloadTarget;
	}

	public ShellSessionSelectionResult ResolveAfterSessionsChanged(
		IEnumerable<SessionModel?> sessions,
		NotifyCollectionChangedAction action,
		SessionModel? selectedSession,
		SessionModel? hotReloadTargetSession)
	{
		var snapshot = sessions.ToList();
		var first = snapshot.FirstOrDefault();
		var resolvedHotReloadTarget = hotReloadTargetSession;
		var resolvedSelectedSession = selectedSession;

		if (action is NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Reset)
		{
			if (resolvedHotReloadTarget != null && !snapshot.Contains(resolvedHotReloadTarget))
			{
				resolvedHotReloadTarget = first;
			}
		}
		else if (action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace &&
				 resolvedHotReloadTarget == null)
		{
			resolvedHotReloadTarget = first;
		}

		if (resolvedSelectedSession != null && !snapshot.Contains(resolvedSelectedSession))
		{
			resolvedSelectedSession = first;
		}
		else if (resolvedSelectedSession == null &&
				 snapshot.Count > 0 &&
				 action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace)
		{
			resolvedSelectedSession = first;
		}

		return new ShellSessionSelectionResult(resolvedSelectedSession, resolvedHotReloadTarget);
	}
}
