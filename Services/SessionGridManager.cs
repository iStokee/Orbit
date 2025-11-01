using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Orbit.Models;

namespace Orbit.Services
{
	/// <summary>
	/// Manages grid-based positioning and snapping of session windows
	/// </summary>
	public class SessionGridManager
	{
		private readonly Dictionary<SessionModel, SessionGridPosition> _sessionPositions = new();

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

		[DllImport("user32.dll")]
		private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

		private const int SW_RESTORE = 9;

		/// <summary>
		/// Snaps sessions to a grid layout based on their assigned positions
		/// </summary>
		public void ApplyGridLayout(IEnumerable<SessionModel> sessions, int viewportWidth, int viewportHeight, int requestedGridDensity)
		{
			if (sessions == null)
				return;

			var sessionList = sessions.Where(s => s.ExternalHandle != IntPtr.Zero).ToList();
			var requested = Math.Clamp(requestedGridDensity, 1, 3);
			var density = ResolveDensity(sessionList, requested);

			foreach (var session in sessionList)
			{
				if (_sessionPositions.TryGetValue(session, out var position) && position != SessionGridPosition.None)
				{
					var bounds = CalculateBounds(position, viewportWidth, viewportHeight, density);
					PositionWindow(session.ExternalHandle, bounds);
				}
			}
		}

		/// <summary>
		/// Auto-assigns sessions to grid positions (fills corners first, then edges)
		/// </summary>
		public void AutoAssignGrid(IEnumerable<SessionModel> sessions, int gridDensity, SessionGridOverflowPolicy overflowPolicy)
		{
			if (sessions == null)
				return;

			var sessionList = sessions.ToList();
			if (sessionList.Count == 0)
				return;

			var density = Math.Clamp(gridDensity, 1, 3);
			var positions = GetPositionsForDensity(density).ToList();
			var capacity = positions.Count;

			if (overflowPolicy == SessionGridOverflowPolicy.AutoSplit && sessionList.Count > capacity && density < 3)
			{
				density = Math.Min(3, density + 1);
				positions = GetPositionsForDensity(density).ToList();
				capacity = positions.Count;
			}

			_sessionPositions.Clear();

			for (int i = 0; i < sessionList.Count; i++)
			{
				var session = sessionList[i];

				if (i < capacity)
				{
					_sessionPositions[session] = positions[i];
					continue;
				}

				switch (overflowPolicy)
				{
					case SessionGridOverflowPolicy.Stack:
						_sessionPositions[session] = positions[^1];
						break;
					case SessionGridOverflowPolicy.AutoSplit:
						_sessionPositions[session] = positions[i % capacity];
						break;
					default:
						_sessionPositions[session] = SessionGridPosition.None;
						break;
				}
			}
		}

		/// <summary>
		/// Sets a specific position for a session
		/// </summary>
		public bool SetSessionPosition(
			SessionModel session,
			SessionGridPosition position,
			SessionGridConflictResolution conflictResolution,
			Func<SessionModel, SessionModel, SessionGridPosition, bool>? conflictPrompt = null)
		{
			if (session == null)
				return false;

			if (position == SessionGridPosition.None)
			{
				_sessionPositions.Remove(session);
				return true;
			}

			var conflictingEntry = _sessionPositions.FirstOrDefault(kvp => kvp.Value == position && !ReferenceEquals(kvp.Key, session));
			if (conflictingEntry.Key != null)
			{
				var shouldReplace = conflictResolution switch
				{
					SessionGridConflictResolution.PreferUserDrop => true,
					SessionGridConflictResolution.PreferSavedLayout => false,
					SessionGridConflictResolution.Prompt => conflictPrompt?.Invoke(conflictingEntry.Key, session, position) ?? false,
					_ => true
				};

				if (!shouldReplace)
				{
					return false;
				}

				_sessionPositions.Remove(conflictingEntry.Key);
			}

			_sessionPositions[session] = position;
			return true;
		}

		/// <summary>
		/// Gets the current grid position for a session
		/// </summary>
		public SessionGridPosition GetSessionPosition(SessionModel session)
		{
			return _sessionPositions.TryGetValue(session, out var position)
				? position
				: SessionGridPosition.None;
		}

		/// <summary>
		/// Clears all grid assignments
		/// </summary>
		public void ClearGrid()
		{
			_sessionPositions.Clear();
		}

		/// <summary>
		/// Calculates window bounds for a given grid position
		/// </summary>
		private (int x, int y, int width, int height) CalculateBounds(SessionGridPosition position, int viewportWidth, int viewportHeight, int gridDensity)
		{
			int halfWidth = viewportWidth / 2;
			int halfHeight = viewportHeight / 2;
			int thirdWidth = viewportWidth / 3;
			int thirdHeight = viewportHeight / 3;

			return position switch
			{
				SessionGridPosition.TopLeft => (0, 0, halfWidth, halfHeight),
				SessionGridPosition.TopRight => (halfWidth, 0, halfWidth, halfHeight),
				SessionGridPosition.BottomLeft => (0, halfHeight, halfWidth, halfHeight),
				SessionGridPosition.BottomRight => (halfWidth, halfHeight, halfWidth, halfHeight),
				SessionGridPosition.Left => (0, 0, halfWidth, viewportHeight),
				SessionGridPosition.Right => (halfWidth, 0, halfWidth, viewportHeight),
				SessionGridPosition.Top => (0, 0, viewportWidth, halfHeight),
				SessionGridPosition.Bottom => (0, halfHeight, viewportWidth, halfHeight),
				SessionGridPosition.TopCenter => (thirdWidth, 0, thirdWidth, thirdHeight),
				SessionGridPosition.MiddleLeft => (0, thirdHeight, thirdWidth, thirdHeight),
				SessionGridPosition.Center => (thirdWidth, thirdHeight, thirdWidth, thirdHeight),
				SessionGridPosition.MiddleRight => (thirdWidth * 2, thirdHeight, thirdWidth, thirdHeight),
				SessionGridPosition.BottomCenter => (thirdWidth, thirdHeight * 2, thirdWidth, thirdHeight),
				SessionGridPosition.Fullscreen => (0, 0, viewportWidth, viewportHeight),
				_ => (0, 0, viewportWidth, viewportHeight)
			};
		}

		/// <summary>
		/// Positions a window using Win32 API
		/// </summary>
		private void PositionWindow(IntPtr hWnd, (int x, int y, int width, int height) bounds)
		{
			if (hWnd == IntPtr.Zero)
				return;

			try
			{
				// Restore window if minimized
				ShowWindow(hWnd, SW_RESTORE);

				// Move and resize
				MoveWindow(hWnd, bounds.x, bounds.y, bounds.width, bounds.height, true);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"Failed to position window: {ex.Message}");
			}
		}

		/// <summary>
		/// Gets a list of all sessions assigned to grid positions
		/// </summary>
		public IReadOnlyDictionary<SessionModel, SessionGridPosition> GetAllAssignments()
		{
			return new Dictionary<SessionModel, SessionGridPosition>(_sessionPositions);
		}

		public void SetAssignments(IDictionary<SessionModel, SessionGridPosition> assignments)
		{
			_sessionPositions.Clear();
			foreach (var pair in assignments)
			{
				if (pair.Key == null)
					continue;

				if (pair.Value == SessionGridPosition.None)
					continue;

				_sessionPositions[pair.Key] = pair.Value;
			}
		}

		private static IEnumerable<SessionGridPosition> GetPositionsForDensity(int density)
		{
			return density switch
			{
				1 => new[] { SessionGridPosition.Fullscreen },
				2 => new[]
				{
					SessionGridPosition.TopLeft,
					SessionGridPosition.TopRight,
					SessionGridPosition.BottomLeft,
					SessionGridPosition.BottomRight,
					SessionGridPosition.Left,
					SessionGridPosition.Right,
					SessionGridPosition.Top,
					SessionGridPosition.Bottom
				},
				_ => new[]
				{
					SessionGridPosition.TopLeft,
					SessionGridPosition.TopCenter,
					SessionGridPosition.TopRight,
					SessionGridPosition.MiddleLeft,
					SessionGridPosition.Center,
					SessionGridPosition.MiddleRight,
					SessionGridPosition.BottomLeft,
					SessionGridPosition.BottomCenter,
					SessionGridPosition.BottomRight
				}
			};
		}

		private int ResolveDensity(IEnumerable<SessionModel> sessions, int requested)
		{
			var density = Math.Clamp(requested, 1, 3);

			foreach (var session in sessions)
			{
				if (!_sessionPositions.TryGetValue(session, out var position))
					continue;

				if (RequiresThreeByThree(position))
				{
					density = Math.Max(density, 3);
				}
				else if (RequiresTwoByTwo(position))
				{
					density = Math.Max(density, 2);
				}
			}

			return density;
		}

		private static bool RequiresThreeByThree(SessionGridPosition position)
		{
			return position is SessionGridPosition.Center
				or SessionGridPosition.TopCenter
				or SessionGridPosition.MiddleLeft
				or SessionGridPosition.MiddleRight
				or SessionGridPosition.BottomCenter;
		}

		private static bool RequiresTwoByTwo(SessionGridPosition position)
		{
			return position is SessionGridPosition.TopLeft
				or SessionGridPosition.TopRight
				or SessionGridPosition.BottomLeft
				or SessionGridPosition.BottomRight
				or SessionGridPosition.Left
				or SessionGridPosition.Right
				or SessionGridPosition.Top
				or SessionGridPosition.Bottom;
		}
	}
}
