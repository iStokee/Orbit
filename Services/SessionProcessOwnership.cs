using System;
using System.Collections.Generic;

namespace Orbit.Services;

/// <summary>Atomic in-process claims made before a detected client is docked.</summary>
internal sealed class SessionProcessOwnership
{
	internal static SessionProcessOwnership Instance { get; } = new();
	private readonly object _sync = new();
	private readonly Dictionary<int, (DateTime StartedUtc, Guid Owner)> _claims = new();

	internal bool TryClaim(int pid, DateTime startedUtc, Guid owner)
	{
		lock (_sync)
		{
			if (_claims.TryGetValue(pid, out var existing) && existing.StartedUtc == startedUtc && existing.Owner != owner) return false;
			_claims[pid] = (startedUtc, owner);
			return true;
		}
	}

	internal void ReleaseOwner(Guid owner)
	{
		lock (_sync)
		{
			foreach (var pid in new List<int>(_claims.Keys))
				if (_claims[pid].Owner == owner) _claims.Remove(pid);
		}
	}
}
