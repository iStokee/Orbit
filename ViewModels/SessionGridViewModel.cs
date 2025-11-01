using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Dragablz;
using Orbit.Models;
using Orbit.Services;
using System.Reflection;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace Orbit.ViewModels
{
	/// <summary>
	/// ViewModel for the Session Grid Layout tool - manages snapping sessions to corners/edges
	/// </summary>
	public class SessionGridViewModel : INotifyPropertyChanged
	{
		private readonly SessionCollectionService _sessionCollectionService;
		private readonly SessionGridManager _gridManager;
		private readonly IInterTabClient _interTabClient;
		private readonly string _partitionKey;
		private readonly Func<int, int, (int width, int height)> _getViewportSize;
		private readonly Dictionary<Guid, SessionGridPosition> _sessionPositions = new();

		// Drop zone state
		private SessionGridPosition _dropZoneCandidate = SessionGridPosition.None;
		private bool _isDropOverlayVisible;
		private bool _isDragInProgress;

		public SessionGridViewModel(
			SessionCollectionService sessionCollectionService,
			SessionGridManager gridManager,
			Func<int, int, (int width, int height)> getViewportSize,
			IInterTabClient? interTabClient,
			string partitionKey)
		{
			_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
			_gridManager = gridManager ?? throw new ArgumentNullException(nameof(gridManager));
			_getViewportSize = getViewportSize ?? throw new ArgumentNullException(nameof(getViewportSize));
			_interTabClient = interTabClient ?? throw new ArgumentNullException(nameof(interTabClient));
			_partitionKey = string.IsNullOrWhiteSpace(partitionKey)
				? throw new ArgumentException("Partition key must be provided.", nameof(partitionKey))
				: partitionKey;

			// Initialize session positions dictionary
			foreach (var session in Sessions)
			{
				_sessionPositions[session.Id] = SessionGridPosition.None;
			}

			// Listen for session collection changes
			_sessionCollectionService.Sessions.CollectionChanged += (s, e) =>
			{
				if (e.NewItems != null)
				{
					foreach (SessionModel session in e.NewItems)
					{
						if (!_sessionPositions.ContainsKey(session.Id))
						{
							_sessionPositions[session.Id] = SessionGridPosition.None;
						}
					}
				}
			};

			// Commands
			AutoAssignCommand = new RelayCommand(_ => AutoAssignSessions());
			ApplyGridCommand = new RelayCommand(_ => ApplyGridLayout());
			ClearGridCommand = new RelayCommand(_ => ClearGrid());

			// Initialize grid cells
			GridCells = new ObservableCollection<GridCellViewModel>();
			RefreshGridCells();

			// Listen for MaxSplitsPerAxis changes to rebuild grid cells
			PropertyChanged += (s, e) =>
			{
				if (e.PropertyName == nameof(MaxSplitsPerAxis))
				{
					RefreshGridCells();
				}
			};
		}

		/// <summary>
		/// Exposes the shared InterTabClient so layout hosts can participate in the main tab partition.
		/// </summary>
		public IInterTabClient InterTabClient => _interTabClient;

		/// <summary>
		/// Partition key that must match the main shell to allow seamless tab movement.
		/// </summary>
		public string PartitionKey => _partitionKey;

		/// <summary>
		/// Gets the shared session collection
		/// </summary>
		public ObservableCollection<SessionModel> Sessions => _sessionCollectionService.Sessions;

		/// <summary>
		/// Gets all available grid positions
		/// </summary>
		public IEnumerable<SessionGridPosition> GridPositions => Enum.GetValues(typeof(SessionGridPosition)).Cast<SessionGridPosition>();

		/// <summary>
		/// Dictionary mapping session IDs to their grid positions
		/// </summary>
		public Dictionary<Guid, SessionGridPosition> SessionPositions => _sessionPositions;

		/// <summary>
		/// Overflow behaviour options for binding.
		/// </summary>
		public IEnumerable<SessionGridOverflowPolicy> OverflowPolicies => Enum.GetValues(typeof(SessionGridOverflowPolicy)).Cast<SessionGridOverflowPolicy>();

		/// <summary>
		/// Conflict resolution options for binding.
		/// </summary>
		public IEnumerable<SessionGridConflictResolution> ConflictResolutions => Enum.GetValues(typeof(SessionGridConflictResolution)).Cast<SessionGridConflictResolution>();

		public SessionGridOverflowPolicy OverflowPolicy
		{
			get => (SessionGridOverflowPolicy)Settings.Default.SessionGridOverflowPolicy;
			set
			{
				if (OverflowPolicy == value)
					return;
				Settings.Default.SessionGridOverflowPolicy = (int)value;
				Settings.Default.Save();
				OnPropertyChanged();
			}
		}

		public SessionGridConflictResolution ConflictResolution
		{
			get => (SessionGridConflictResolution)Settings.Default.SessionGridConflictResolution;
			set
			{
				if (ConflictResolution == value)
					return;
				Settings.Default.SessionGridConflictResolution = (int)value;
				Settings.Default.Save();
				OnPropertyChanged();
			}
		}

		public int MaxSplitsPerAxis
		{
			get => Settings.Default.SessionGridMaxSplitsPerAxis;
			set
			{
				var clamped = Math.Max(1, Math.Min(3, value));
				if (Settings.Default.SessionGridMaxSplitsPerAxis == clamped)
					return;
				Settings.Default.SessionGridMaxSplitsPerAxis = clamped;
				Settings.Default.Save();
				OnPropertyChanged();
				OnPropertyChanged(nameof(PreviewCells));
			}
		}

		public bool StickyLayout
		{
			get => Settings.Default.SessionGridStickyLayout;
			set
			{
				if (Settings.Default.SessionGridStickyLayout == value)
					return;
				Settings.Default.SessionGridStickyLayout = value;
				Settings.Default.Save();
				OnPropertyChanged();
			}
		}

		public IEnumerable<int> PreviewCells => Enumerable.Range(1, MaxSplitsPerAxis * MaxSplitsPerAxis);

		/// <summary>
		/// Collection of grid cells for the interactive drop zone overlay
		/// </summary>
		public ObservableCollection<GridCellViewModel> GridCells { get; }

		/// <summary>
		/// The grid position currently being targeted during a drag operation
		/// </summary>
		public SessionGridPosition DropZoneCandidate
		{
			get => _dropZoneCandidate;
			set
			{
				if (_dropZoneCandidate == value)
					return;
				_dropZoneCandidate = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Whether the drop zone overlay is currently visible
		/// </summary>
		public bool IsDropOverlayVisible
		{
			get => _isDropOverlayVisible;
			set
			{
				if (_isDropOverlayVisible == value)
					return;
				_isDropOverlayVisible = value;
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Whether a drag operation is currently in progress
		/// </summary>
		public bool IsDragInProgress
		{
			get => _isDragInProgress;
			set
			{
				if (_isDragInProgress == value)
					return;
				_isDragInProgress = value;
				OnPropertyChanged();
			}
		}

		// Settings pass-through for XAML binding
		public double DropZonePadding => Settings.Default.SessionGridDropZonePadding;
		public double DropZoneOpacity => Settings.Default.SessionGridDropZoneOpacity;
		public bool ShowAllZonesOnDrag => Settings.Default.SessionGridShowAllZonesOnDrag;

		/// <summary>
		/// Command to auto-assign sessions to grid positions
		/// </summary>
		public ICommand AutoAssignCommand { get; }

		/// <summary>
		/// Command to apply the current grid layout
		/// </summary>
		public ICommand ApplyGridCommand { get; }

		/// <summary>
		/// Command to clear all grid assignments
		/// </summary>
		public ICommand ClearGridCommand { get; }

		/// <summary>
		/// Auto-assigns sessions to grid positions
		/// </summary>
	private void AutoAssignSessions()
	{
		_gridManager.AutoAssignGrid(Sessions, MaxSplitsPerAxis, OverflowPolicy);

		// Sync with our local dictionary
		_sessionPositions.Clear();
		foreach (var session in Sessions)
		{
				_sessionPositions[session.Id] = _gridManager.GetSessionPosition(session);
			}

			UpdateCellSessionCounts();
			OnPropertyChanged(nameof(SessionPositions));
		}

		/// <summary>
		/// Applies the grid layout to all sessions
		/// </summary>
		private void ApplyGridLayout()
		{
			var previousAssignments = StickyLayout
				? null
				: _gridManager.GetAllAssignments();

			// Update grid manager with current assignments
			foreach (var session in Sessions)
			{
				if (_sessionPositions.TryGetValue(session.Id, out var position))
				{
					_gridManager.SetSessionPosition(
						session,
						position,
						ConflictResolution,
						ConflictResolution == SessionGridConflictResolution.Prompt ? PromptForConflict : null);
				}
			}

			SyncSessionPositionsFromManager();

			// Get viewport size (use default if callback returns 0)
			var (width, height) = _getViewportSize(0, 0);
			if (width == 0 || height == 0)
			{
				// Fallback to screen dimensions
				width = (int)System.Windows.SystemParameters.PrimaryScreenWidth;
				height = (int)System.Windows.SystemParameters.PrimaryScreenHeight;
			}

			// Apply the layout
			_gridManager.ApplyGridLayout(Sessions, width, height, MaxSplitsPerAxis);

			if (!StickyLayout && previousAssignments != null)
			{
				_gridManager.SetAssignments((IDictionary<SessionModel, SessionGridPosition>)previousAssignments);
				SyncSessionPositionsFromManager();
			}
		}

		/// <summary>
		/// Clears all grid assignments
		/// </summary>
		private void ClearGrid()
		{
			_gridManager.ClearGrid();

			foreach (var key in _sessionPositions.Keys.ToList())
			{
				_sessionPositions[key] = SessionGridPosition.None;
			}

			UpdateCellSessionCounts();
			OnPropertyChanged(nameof(SessionPositions));
		}

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		private void SyncSessionPositionsFromManager()
		{
			_sessionPositions.Clear();
			foreach (var session in Sessions)
			{
				_sessionPositions[session.Id] = _gridManager.GetSessionPosition(session);
			}

			OnPropertyChanged(nameof(SessionPositions));
		}

		private bool PromptForConflict(SessionModel currentOwner, SessionModel incoming, SessionGridPosition position)
		{
			var positionName = GetPositionDescription(position);
			var currentName = string.IsNullOrWhiteSpace(currentOwner?.Name) ? "Existing session" : currentOwner.Name;
			var incomingName = string.IsNullOrWhiteSpace(incoming?.Name) ? "New session" : incoming.Name;
			var message = $"\"{currentName}\" already occupies {positionName}.\nReplace it with \"{incomingName}\"?";
			var result = MessageBox.Show(
				message,
				"Session Grid Conflict",
				MessageBoxButton.YesNo,
				MessageBoxImage.Question);
			return result == MessageBoxResult.Yes;
		}

		private static string GetPositionDescription(SessionGridPosition position)
		{
			var memberInfo = typeof(SessionGridPosition).GetMember(position.ToString()).FirstOrDefault();
			if (memberInfo != null)
			{
				var descriptionAttribute = memberInfo.GetCustomAttribute<DescriptionAttribute>();
				if (descriptionAttribute != null)
				{
					return descriptionAttribute.Description;
				}
			}

			return position.ToString();
		}

		/// <summary>
		/// Rebuilds the GridCells collection based on the current MaxSplitsPerAxis setting
		/// </summary>
		private void RefreshGridCells()
		{
			GridCells.Clear();

			// Add cells based on grid density
			switch (MaxSplitsPerAxis)
			{
				case 1:
					GridCells.Add(new GridCellViewModel(SessionGridPosition.Fullscreen));
					break;
				case 2:
					GridCells.Add(new GridCellViewModel(SessionGridPosition.TopLeft));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.TopRight));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.BottomLeft));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.BottomRight));
					break;
				case 3:
					GridCells.Add(new GridCellViewModel(SessionGridPosition.TopLeft));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.TopCenter));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.TopRight));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.MiddleLeft));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.Center));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.MiddleRight));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.BottomLeft));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.BottomCenter));
					GridCells.Add(new GridCellViewModel(SessionGridPosition.BottomRight));
					break;
			}

			UpdateCellSessionCounts();
		}

		/// <summary>
		/// Updates the session count for each grid cell based on current assignments
		/// </summary>
		private void UpdateCellSessionCounts()
		{
			foreach (var cell in GridCells)
			{
				var count = _sessionPositions.Count(kvp => kvp.Value == cell.Position);
				cell.UpdateSessionCount(count);
			}
		}

		/// <summary>
		/// Assigns a session to a specific grid position (called when session is dropped)
		/// </summary>
		public void AssignSessionToPosition(Guid sessionId, SessionGridPosition position)
		{
			if (!_sessionPositions.ContainsKey(sessionId))
				_sessionPositions[sessionId] = SessionGridPosition.None;

			_sessionPositions[sessionId] = position;
			UpdateCellSessionCounts();
			OnPropertyChanged(nameof(SessionPositions));

			// If sticky layout, update the grid manager immediately
			if (StickyLayout)
			{
				var session = Sessions.FirstOrDefault(s => s.Id == sessionId);
				if (session != null)
				{
					_gridManager.SetSessionPosition(session, position, ConflictResolution, null);
				}
			}
		}
	}
}
