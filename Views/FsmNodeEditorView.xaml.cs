using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Orbit.Models;
using Orbit.ViewModels;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class FsmNodeEditorView : UserControl
{
	private bool _isPanning;
	private Point _panStart;
	private double _panStartHorizontal;
	private double _panStartVertical;

	// Box selection state
	private bool _isBoxSelecting;
	private Point _boxSelectStart;
	private Rectangle? _selectionBox;

	// Zoom state
	private double _zoomLevel = 1.0;
	private const double ZoomMin = 0.25;
	private const double ZoomMax = 3.0;
	private const double ZoomStep = 0.1;

	public FsmNodeEditorView()
	{
		InitializeComponent();
	}

	#region Zoom Handlers

	private void CanvasScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
			return;

		var zoomDelta = e.Delta > 0 ? ZoomStep : -ZoomStep;
		ApplyZoom(_zoomLevel + zoomDelta);
		e.Handled = true;
	}

	private void ZoomInButton_Click(object sender, RoutedEventArgs e)
	{
		ApplyZoom(_zoomLevel + ZoomStep);
	}

	private void ZoomOutButton_Click(object sender, RoutedEventArgs e)
	{
		ApplyZoom(_zoomLevel - ZoomStep);
	}

	private void ZoomResetButton_Click(object sender, RoutedEventArgs e)
	{
		ApplyZoom(1.0);
	}

	private void ApplyZoom(double newZoom)
	{
		_zoomLevel = Math.Clamp(newZoom, ZoomMin, ZoomMax);
		CanvasScaleTransform.ScaleX = _zoomLevel;
		CanvasScaleTransform.ScaleY = _zoomLevel;
		ZoomLevelText.Text = $"{(int)(_zoomLevel * 100)}%";
	}

	#endregion

	private void NodeMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (DataContext is not FsmNodeEditorViewModel vm)
			return;

		if (sender is Thumb thumb && thumb.Tag is FsmNodeModel node)
		{
			var toggle = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
			vm.SelectNode(node, toggle);
			e.Handled = true;
		}
	}

	private void NodeDragDelta(object sender, DragDeltaEventArgs e)
	{
		if (DataContext is not FsmNodeEditorViewModel vm || sender is not Thumb thumb || thumb.Tag is not FsmNodeModel node)
			return;

		var nodesToMove = vm.SelectedNodes.Contains(node) && vm.SelectedNodes.Count > 0
			? vm.SelectedNodes
			: new ReadOnlyCollection<FsmNodeModel>(new List<FsmNodeModel> { node });

		foreach (var n in nodesToMove)
		{
			n.X = System.Math.Max(0, n.X + e.HorizontalChange);
			n.Y = System.Math.Max(0, n.Y + e.VerticalChange);
		}
	}

	private void CanvasScrollViewer_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (sender is not ScrollViewer scroller)
			return;

		// Panning with middle mouse or space + left click
		if (e.MiddleButton == MouseButtonState.Pressed || (e.LeftButton == MouseButtonState.Pressed && Keyboard.IsKeyDown(Key.Space)))
		{
			_isPanning = true;
			_panStart = e.GetPosition(scroller);
			_panStartHorizontal = scroller.HorizontalOffset;
			_panStartVertical = scroller.VerticalOffset;
			scroller.CaptureMouse();
			scroller.Cursor = Cursors.ScrollAll;
			e.Handled = true;
			return;
		}

		// Box selection with left click on canvas background (not on a node)
		if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is FrameworkElement element)
		{
			// Check if we clicked on the canvas background (Grid or ScrollViewer itself)
			var isCanvasBackground = element.DataContext is FsmNodeEditorViewModel ||
			                         element is Grid ||
			                         element is ScrollViewer ||
			                         element.Name == "CanvasScrollViewer";

			if (isCanvasBackground && DataContext is FsmNodeEditorViewModel vm)
			{
				// Clear selection on background click (unless Ctrl is held for box select)
				if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
				{
					vm.ClearSelection();
				}

				// Start box selection
				_isBoxSelecting = true;
				_boxSelectStart = e.GetPosition(CanvasContainer);

				// Create selection box visual
				if (_selectionBox == null)
				{
					_selectionBox = new Rectangle
					{
						Stroke = new SolidColorBrush(Color.FromArgb(180, 0, 122, 204)),
						StrokeThickness = 1,
						Fill = new SolidColorBrush(Color.FromArgb(40, 0, 122, 204)),
						StrokeDashArray = new DoubleCollection { 4, 2 },
						IsHitTestVisible = false
					};
				}

				scroller.CaptureMouse();
				e.Handled = true;
			}
		}
	}

	private void CanvasScrollViewer_OnPreviewMouseMove(object sender, MouseEventArgs e)
	{
		if (sender is not ScrollViewer scroller)
			return;

		// Handle panning
		if (_isPanning)
		{
			var current = e.GetPosition(scroller);
			var delta = current - _panStart;

			scroller.ScrollToHorizontalOffset(_panStartHorizontal - delta.X);
			scroller.ScrollToVerticalOffset(_panStartVertical - delta.Y);
			e.Handled = true;
			return;
		}

		// Handle box selection
		if (_isBoxSelecting && _selectionBox != null)
		{
			var current = e.GetPosition(CanvasContainer);
			var x = Math.Min(_boxSelectStart.X, current.X);
			var y = Math.Min(_boxSelectStart.Y, current.Y);
			var width = Math.Abs(current.X - _boxSelectStart.X);
			var height = Math.Abs(current.Y - _boxSelectStart.Y);

			Canvas.SetLeft(_selectionBox, x);
			Canvas.SetTop(_selectionBox, y);
			_selectionBox.Width = width;
			_selectionBox.Height = height;

			// Add to visual tree if not already added
			if (SelectionOverlay != null && !SelectionOverlay.Children.Contains(_selectionBox))
			{
				SelectionOverlay.Children.Add(_selectionBox);
			}

			e.Handled = true;
		}
	}

	private void CanvasScrollViewer_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
	{
		if (sender is not ScrollViewer scroller)
			return;

		// End panning
		if (_isPanning)
		{
			_isPanning = false;
			scroller.ReleaseMouseCapture();
			scroller.Cursor = Cursors.Arrow;
			e.Handled = true;
			return;
		}

		// End box selection
		if (_isBoxSelecting)
		{
			_isBoxSelecting = false;
			scroller.ReleaseMouseCapture();

			if (_selectionBox != null && DataContext is FsmNodeEditorViewModel vm)
			{
				// Get selection bounds
				var selectRect = new Rect(
					Canvas.GetLeft(_selectionBox),
					Canvas.GetTop(_selectionBox),
					_selectionBox.Width,
					_selectionBox.Height);

				// Select nodes within bounds
				if (selectRect.Width > 5 && selectRect.Height > 5)
				{
					vm.SelectNodesInBounds(selectRect);
				}

				// Remove selection box from canvas
				SelectionOverlay?.Children.Remove(_selectionBox);
			}

			e.Handled = true;
		}
	}
}
