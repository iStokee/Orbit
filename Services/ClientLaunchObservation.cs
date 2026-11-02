using System;
using System.Collections.Generic;
using System.Linq;

namespace Orbit.Services;

/// <summary>A URI activation is a request, not a game-process identity.</summary>
internal sealed class ClientLaunchObservation
{
	private readonly HashSet<int> _existingClients;
	internal DateTime StartedUtc { get; }
	internal ClientLaunchObservation(IEnumerable<int> existingClients, DateTime startedUtc)
	{
		_existingClients = new HashSet<int>(existingClients);
		StartedUtc = startedUtc;
	}

	internal bool IsFresh(int pid, DateTime startedUtc)
		=> !_existingClients.Contains(pid) && startedUtc >= StartedUtc;

	internal static int? SelectCandidate(IEnumerable<(int Pid, bool IsLauncherChild)> candidates)
	{
		var fresh = candidates.ToArray();
		var children = fresh.Where(c => c.IsLauncherChild).ToArray();
		if (children.Length == 1) return children[0].Pid;
		return children.Length == 0 && fresh.Length == 1 ? fresh[0].Pid : null;
	}
}
