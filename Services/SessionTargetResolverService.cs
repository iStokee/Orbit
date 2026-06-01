using System.Collections.Generic;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

public sealed class SessionTargetResolverService
{
	public SessionModel? ResolveHotReloadTarget(
		IEnumerable<SessionModel?> sessions,
		SessionModel? sharedHotReloadTarget,
		SessionModel? localHotReloadTarget,
		SessionModel? selectedSession,
		SessionModel? globalSelectedSession)
	{
		var snapshot = sessions.ToList();

		if (sharedHotReloadTarget != null && snapshot.Contains(sharedHotReloadTarget))
		{
			return sharedHotReloadTarget;
		}

		if (localHotReloadTarget != null && snapshot.Contains(localHotReloadTarget))
		{
			return localHotReloadTarget;
		}

		if (selectedSession != null && snapshot.Contains(selectedSession))
		{
			return selectedSession;
		}

		if (globalSelectedSession != null && snapshot.Contains(globalSelectedSession))
		{
			return globalSelectedSession;
		}

		return snapshot.FirstOrDefault();
	}
}
