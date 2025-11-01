using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Dragablz;
using Orbit.Models;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using DragEventArgs = System.Windows.DragEventArgs;
using IDataObject = System.Windows.IDataObject;
using Point = System.Windows.Point;
using DragDropEffects = System.Windows.DragDropEffects;

namespace Orbit.Views
{
	/// <summary>
	/// Interaction logic for SessionGridView.xaml
	/// </summary>
	public partial class SessionGridView : UserControl
	{
		public SessionGridView(SessionGridViewModel viewModel)
		{
			InitializeComponent();
			DataContext = viewModel;

			// Wire up drag-and-drop events
			GridDropZone.DragEnter += GridDropZone_DragEnter;
			GridDropZone.DragOver += GridDropZone_DragOver;
			GridDropZone.DragLeave += GridDropZone_DragLeave;
			GridDropZone.Drop += GridDropZone_Drop;
		}

		private void GridDropZone_DragEnter(object sender, DragEventArgs e)
		{
			if (DataContext is not SessionGridViewModel viewModel)
				return;

			// Check if dragging a session tab
			if (IsDraggingSession(e))
			{
				viewModel.IsDragInProgress = true;
				viewModel.IsDropOverlayVisible = true;
				e.Effects = DragDropEffects.Move;
			}
			else
			{
				e.Effects = DragDropEffects.None;
			}

			e.Handled = true;
		}

		private void GridDropZone_DragOver(object sender, DragEventArgs e)
		{
			if (DataContext is not SessionGridViewModel viewModel)
				return;

			if (!viewModel.IsDragInProgress)
				return;

			// Get cursor position relative to GridCellContainer
			var position = e.GetPosition(GridCellContainer);
			var targetCell = DetectGridCell(position, GridCellContainer.ActualWidth, GridCellContainer.ActualHeight);

			// Update highlight
			foreach (var cell in viewModel.GridCells)
			{
				cell.IsDropTarget = cell.Position == targetCell;
			}

			viewModel.DropZoneCandidate = targetCell;
			e.Effects = targetCell != SessionGridPosition.None ? DragDropEffects.Move : DragDropEffects.None;
			e.Handled = true;
		}

		private void GridDropZone_DragLeave(object sender, DragEventArgs e)
		{
			if (DataContext is not SessionGridViewModel viewModel)
				return;

			viewModel.IsDragInProgress = false;
			viewModel.IsDropOverlayVisible = false;
			viewModel.DropZoneCandidate = SessionGridPosition.None;

			foreach (var cell in viewModel.GridCells)
			{
				cell.IsDropTarget = false;
			}

			e.Handled = true;
		}

		private void GridDropZone_Drop(object sender, DragEventArgs e)
		{
			if (DataContext is not SessionGridViewModel viewModel)
				return;

			var position = e.GetPosition(GridCellContainer);
			var targetCell = DetectGridCell(position, GridCellContainer.ActualWidth, GridCellContainer.ActualHeight);

			if (targetCell != SessionGridPosition.None)
			{
				var session = ExtractSessionFromDragData(e.Data);
				if (session != null)
				{
					viewModel.AssignSessionToPosition(session.Id, targetCell);
				}
			}

			// Reset state
			viewModel.IsDragInProgress = false;
			viewModel.IsDropOverlayVisible = false;
			viewModel.DropZoneCandidate = SessionGridPosition.None;

			foreach (var cell in viewModel.GridCells)
			{
				cell.IsDropTarget = false;
			}

			e.Handled = true;
		}

		private SessionGridPosition DetectGridCell(Point cursorPos, double width, double height)
		{
			if (DataContext is not SessionGridViewModel viewModel)
				return SessionGridPosition.None;

			var gridSize = viewModel.MaxSplitsPerAxis;
			var cellWidth = width / gridSize;
			var cellHeight = height / gridSize;

			var col = (int)(cursorPos.X / cellWidth);
			var row = (int)(cursorPos.Y / cellHeight);

			// Clamp to grid bounds
			col = Math.Clamp(col, 0, gridSize - 1);
			row = Math.Clamp(row, 0, gridSize - 1);

			// Map row/col to SessionGridPosition based on grid size
			return MapCellToPosition(row, col, gridSize);
		}

		private SessionGridPosition MapCellToPosition(int row, int col, int gridSize)
		{
			if (gridSize == 1)
				return SessionGridPosition.Fullscreen;

			if (gridSize == 2)
			{
				if (row == 0 && col == 0) return SessionGridPosition.TopLeft;
				if (row == 0 && col == 1) return SessionGridPosition.TopRight;
				if (row == 1 && col == 0) return SessionGridPosition.BottomLeft;
				if (row == 1 && col == 1) return SessionGridPosition.BottomRight;
			}

			if (gridSize == 3)
			{
				if (row == 0 && col == 0) return SessionGridPosition.TopLeft;
				if (row == 0 && col == 1) return SessionGridPosition.TopCenter;
				if (row == 0 && col == 2) return SessionGridPosition.TopRight;
				if (row == 1 && col == 0) return SessionGridPosition.MiddleLeft;
				if (row == 1 && col == 1) return SessionGridPosition.Center;
				if (row == 1 && col == 2) return SessionGridPosition.MiddleRight;
				if (row == 2 && col == 0) return SessionGridPosition.BottomLeft;
				if (row == 2 && col == 1) return SessionGridPosition.BottomCenter;
				if (row == 2 && col == 2) return SessionGridPosition.BottomRight;
			}

			return SessionGridPosition.None;
		}

		private bool IsDraggingSession(DragEventArgs e)
		{
			// Check if Dragablz tab data is present
			return e.Data.GetDataPresent(typeof(DragablzItem)) ||
				   e.Data.GetDataPresent(typeof(SessionModel));
		}

		private SessionModel? ExtractSessionFromDragData(IDataObject data)
		{
			if (data.GetDataPresent(typeof(SessionModel)))
			{
				return data.GetData(typeof(SessionModel)) as SessionModel;
			}

			if (data.GetDataPresent(typeof(DragablzItem)))
			{
				var item = data.GetData(typeof(DragablzItem)) as DragablzItem;
				return item?.DataContext as SessionModel;
			}

			return null;
		}
	}
}
