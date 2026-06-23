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

	// Tool tabs are tracked separately, keyed by their stable Key (Stage 2e). Sessions key by
	// Guid; tools by Key. Tools deliberately do not raise PlacementChanged (the ownership
	// coordinator is session-only) but share the same data-driven oracle.
	private readonly Dictionary<string, SessionPlacementKind> _toolPlacements = new(StringComparer.Ordinal);

	public IDisposable BeginMove(SessionModel session, SessionPlacementKind target)
	{
		if (session == null)
		{
			throw new ArgumentNullException(nameof(session));
		}

		return new MoveScope(this, session, target);
	}

	/// <summary>
	/// Opens a "move in progress" grace for a session whose destination host is not yet known
	/// (e.g. a Dragablz drag-out, where the landing host is only known once the matching add
	/// arrives). Unlike <see cref="BeginMove"/> this deliberately does NOT change the recorded
	/// placement. Pairs with <see cref="EndExternalMove"/> and is idempotent.
	/// </summary>
	public void BeginExternalMove(SessionModel session, string? reason = null)
	{
		if (session == null)
		{
			return;
		}

		bool opened;
		lock (_sync)
		{
			opened = _movingSessions.Add(session.Id);
		}

		if (opened)
		{
			LogPlacement($"{GetSessionLogIdentity(session)} external-move open{FormatReason(reason)}");
		}
	}

	/// <summary>
	/// Closes a grace opened by <see cref="BeginExternalMove"/>. Idempotent: safe to call from
	/// the landing add, from final orphan validation, or both.
	/// </summary>
	public void EndExternalMove(SessionModel session, string? reason = null)
	{
		if (session == null)
		{
			return;
		}

		bool closed;
		lock (_sync)
		{
			closed = _movingSessions.Remove(session.Id);
		}

		if (closed)
		{
			LogPlacement($"{GetSessionLogIdentity(session)} external-move close placement={GetPlacement(session)}{FormatReason(reason)}");
		}
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

	// --- Ownership oracle (Stage 2) ---------------------------------------------------------
	// Data-driven answers to "where does this session live", replacing visual-tree scraping.
	// With move paths now maintaining placement transactionally, these are the authoritative
	// predicates the shell/reconcile consumers read.

	/// <summary>
	/// True when the session is placed in some visible host — main tab strip, an Orbit workspace
	/// cell, or a tear-off window. Replaces the scrape's <c>HasAnyUiReference</c>.
	/// </summary>
	public bool IsPlacedInHost(SessionModel session)
		=> GetPlacement(session) is SessionPlacementKind.MainTabs
			or SessionPlacementKind.OrbitWorkspace
			or SessionPlacementKind.TearOffWindow;

	/// <summary>True when the session is placed in the Orbit workspace.</summary>
	public bool IsInOrbitWorkspace(SessionModel session)
		=> GetPlacement(session) == SessionPlacementKind.OrbitWorkspace;

	/// <summary>
	/// True when the session is placed in a non-Orbit host (main tab strip or a tear-off window).
	/// Replaces the scrape's <c>IsInNonOrbitTabs</c>.
	/// </summary>
	public bool IsInNonOrbitHost(SessionModel session)
		=> GetPlacement(session) is SessionPlacementKind.MainTabs
			or SessionPlacementKind.TearOffWindow;

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

	// --- Tool placement (Stage 2e) ----------------------------------------------------------
	// Same data-driven oracle for tool tabs so the Orbit reconcile loop reads tool ownership
	// from data instead of scraping the visual tree.

	public void SetPlacement(ToolTabItem tool, SessionPlacementKind placement, string? reason = null)
	{
		if (tool?.Key is not string key)
		{
			return;
		}

		SessionPlacementKind previous;
		lock (_sync)
		{
			previous = _toolPlacements.TryGetValue(key, out var existing)
				? existing
				: SessionPlacementKind.Unknown;
			if (previous == placement)
			{
				return;
			}

			_toolPlacements[key] = placement;
		}

		LogPlacement($"tool='{tool.Name}' key={key} {previous} -> {placement}{FormatReason(reason)}");
	}

	public SessionPlacementKind GetPlacement(ToolTabItem tool)
	{
		if (tool?.Key is not string key)
		{
			return SessionPlacementKind.Unknown;
		}

		lock (_sync)
		{
			return _toolPlacements.TryGetValue(key, out var placement)
				? placement
				: SessionPlacementKind.Unknown;
		}
	}

	/// <summary>True when the tool is placed in a non-Orbit host (main tab strip or tear-off).</summary>
	public bool IsInNonOrbitHost(ToolTabItem tool)
		=> GetPlacement(tool) is SessionPlacementKind.MainTabs or SessionPlacementKind.TearOffWindow;

	/// <summary>True when the tool is placed in the Orbit workspace.</summary>
	public bool IsInOrbitWorkspace(ToolTabItem tool)
		=> GetPlacement(tool) == SessionPlacementKind.OrbitWorkspace;

	public void Remove(ToolTabItem tool)
	{
		if (tool?.Key is not string key)
		{
			return;
		}

		lock (_sync)
		{
			_toolPlacements.Remove(key);
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
