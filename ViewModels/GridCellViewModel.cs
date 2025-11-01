using System.ComponentModel;
using System.Runtime.CompilerServices;
using Orbit.Models;

namespace Orbit.ViewModels
{
	/// <summary>
	/// Represents a single cell in the Session Grid drop zone overlay.
	/// Displays position information and provides visual feedback during drag operations.
	/// </summary>
	public class GridCellViewModel : INotifyPropertyChanged
	{
		private bool _isDropTarget;
		private int _sessionCount;

		/// <summary>
		/// The grid position this cell represents
		/// </summary>
		public SessionGridPosition Position { get; }

		/// <summary>
		/// Number of sessions currently assigned to this position
		/// </summary>
		public int SessionCount
		{
			get => _sessionCount;
			private set
			{
				if (_sessionCount == value)
					return;
				_sessionCount = value;
				OnPropertyChanged();
				OnPropertyChanged(nameof(HasSessions));
			}
		}

		/// <summary>
		/// Whether this cell has any sessions assigned to it
		/// </summary>
		public bool HasSessions => SessionCount > 0;

		/// <summary>
		/// Whether this cell is currently the drop target during a drag operation
		/// </summary>
		public bool IsDropTarget
		{
			get => _isDropTarget;
			set
			{
				if (_isDropTarget == value)
					return;
				_isDropTarget = value;
				OnPropertyChanged();
			}
		}

		public GridCellViewModel(SessionGridPosition position)
		{
			Position = position;
			_sessionCount = 0;
			_isDropTarget = false;
		}

		/// <summary>
		/// Updates the session count for this cell
		/// </summary>
		public void UpdateSessionCount(int count)
		{
			SessionCount = count;
		}

		public event PropertyChangedEventHandler? PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
