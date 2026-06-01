using System;
using System.Collections.Generic;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

public sealed class SessionNameService
{
	private const string DefaultSessionPrefix = "RuneScape Session ";
	private readonly object sync = new();
	private int nextSessionOrdinal = 1;

	public string ResolveSessionName(IEnumerable<SessionModel?> sessions, string? preferredName)
	{
		if (sessions == null)
		{
			throw new ArgumentNullException(nameof(sessions));
		}

		var snapshot = sessions.ToList();
		if (!string.IsNullOrWhiteSpace(preferredName))
		{
			var requested = preferredName.Trim();
			if (!ContainsSessionName(snapshot, requested))
			{
				return requested;
			}
		}

		lock (sync)
		{
			var maxObserved = snapshot
				.Select(s => TryParseDefaultSessionOrdinal(s?.Name))
				.Where(v => v.HasValue)
				.Select(v => v!.Value)
				.DefaultIfEmpty(0)
				.Max();

			if (nextSessionOrdinal <= maxObserved)
			{
				nextSessionOrdinal = maxObserved + 1;
			}

			while (true)
			{
				var candidate = $"{DefaultSessionPrefix}{nextSessionOrdinal++}";
				if (!ContainsSessionName(snapshot, candidate))
				{
					return candidate;
				}
			}
		}
	}

	internal static int? TryParseDefaultSessionOrdinal(string? name)
	{
		if (string.IsNullOrWhiteSpace(name) || !name.StartsWith(DefaultSessionPrefix, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var suffix = name.Substring(DefaultSessionPrefix.Length).Trim();
		return int.TryParse(suffix, out var parsed) && parsed > 0 ? parsed : null;
	}

	private static bool ContainsSessionName(IEnumerable<SessionModel?> sessions, string candidate)
	{
		return sessions.Any(s => string.Equals(s?.Name, candidate, StringComparison.OrdinalIgnoreCase));
	}
}
