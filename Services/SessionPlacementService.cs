using System;
using System.Collections.Generic;
using System.Linq;
using Orbit.Models;

namespace Orbit.Services;

public enum SessionPlacementKind
{
	Unknown = 0,
	MainTabs = 1,
	OrbitWorkspace = 2,
	TearOffWindow = 3,
	Closing = 4
}

/// <summary>
/// Tracks where a live session is intended to be displayed. This is deliberately separate
/// from session lifetime so transient Dragablz moves cannot be interpreted as process close.
/// </summary>
public sealed class SessionPlacementService
{
	public event EventHandler<SessionPlacementChangedEventArgs>? PlacementChanged;

	private sealed class MoveScope : IDisposable
	{
		private readonly SessionPlacementService _owner;
		private readonly SessionModel _session;
		private bool _disposed;

		public MoveScope(SessionPlacementService owner, SessionModel session, SessionPlacementKind target)
		{
			_owner = owner;
			_session = session;
			lock (_owner._sync)
			{
				_owner._movingSessions.Add(session.Id);
			}
			_owner.SetPlacement(session, target, "move-start");
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			lock (_owner._sync)
			{
				_owner._movingSessions.Remove(_session.Id);
			}
			LogPlacement($"move-end session={_owner.GetSessionLogIdentity(_session)} placement={_owner.GetPlacement(_session)}");
		}
	}

	private readonly object _sync = new();
	private readonly Dictionary<Guid, SessionPlacementKind> _placements = new();
	private readonly HashSet<Guid> _movingSessions = new();

	public IDisposable BeginMove(SessionModel session, SessionPlacementKind target)
	{
		if (session == null)
		{
			throw new ArgumentNullException(nameof(session));
		}

		return new MoveScope(this, session, target);
	}

	public void SetPlacement(SessionModel session, SessionPlacementKind placement, string? reason = null)
	{
		if (session == null)
		{
			return;
		}

		SessionPlacementKind previous;
		lock (_sync)
		{
			previous = _placements.TryGetValue(session.Id, out var existing)
				? existing
				: SessionPlacementKind.Unknown;
			if (previous == placement)
			{
				return;
			}

			_placements[session.Id] = placement;
		}

		LogPlacement($"{GetSessionLogIdentity(session)} {previous} -> {placement}{FormatReason(reason)}");
		PlacementChanged?.Invoke(this, new SessionPlacementChangedEventArgs(session, previous, placement, reason));
	}

	public SessionPlacementKind GetPlacement(SessionModel session)
	{
		if (session == null)
		{
			return SessionPlacementKind.Unknown;
		}

		lock (_sync)
		{
			return _placements.TryGetValue(session.Id, out var placement)
				? placement
				: SessionPlacementKind.Unknown;
		}
	}

	public bool IsMoveInProgress(SessionModel session)
	{
		if (session == null)
		{
			return false;
		}

		lock (_sync)
		{
			return _movingSessions.Contains(session.Id);
		}
	}

	public void Remove(SessionModel session)
	{
		if (session == null)
		{
			return;
		}

		SessionPlacementKind previous;
		lock (_sync)
		{
			previous = _placements.TryGetValue(session.Id, out var existing)
				? existing
				: SessionPlacementKind.Unknown;
			_placements.Remove(session.Id);
			_movingSessions.Remove(session.Id);
		}

		if (previous != SessionPlacementKind.Unknown)
		{
			LogPlacement($"{GetSessionLogIdentity(session)} {previous} -> Unknown (removed)");
			PlacementChanged?.Invoke(this, new SessionPlacementChangedEventArgs(session, previous, SessionPlacementKind.Unknown, "removed"));
		}
	}

	public IReadOnlyList<SessionPlacementSnapshot> GetSnapshot()
	{
		lock (_sync)
		{
			return _placements
				.Select(entry => new SessionPlacementSnapshot(entry.Key, entry.Value, _movingSessions.Contains(entry.Key)))
				.ToArray();
		}
	}

	private string GetSessionLogIdentity(SessionModel session)
	{
		var pid = session.RSProcess?.Id.ToString() ?? "n/a";
		var handle = session.ExternalHandle == nint.Zero ? "n/a" : $"0x{session.ExternalHandle:X}";
		return $"session='{session.Name ?? session.Id.ToString()}' id={session.Id:N} pid={pid} hwnd={handle}";
	}

	private static string FormatReason(string? reason)
		=> string.IsNullOrWhiteSpace(reason) ? string.Empty : $" ({reason.Trim()})";

	private static void LogPlacement(string message)
		=> Console.WriteLine($"[Orbit][Placement] {message}");
}

public sealed class SessionPlacementChangedEventArgs : EventArgs
{
	public SessionPlacementChangedEventArgs(
		SessionModel session,
		SessionPlacementKind previous,
		SessionPlacementKind current,
		string? reason)
	{
		Session = session;
		Previous = previous;
		Current = current;
		Reason = reason;
	}

	public SessionModel Session { get; }
	public SessionPlacementKind Previous { get; }
	public SessionPlacementKind Current { get; }
	public string? Reason { get; }
}

public sealed record SessionPlacementSnapshot(
	Guid SessionId,
	SessionPlacementKind Placement,
	bool IsMoveInProgress);
