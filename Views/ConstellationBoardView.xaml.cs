using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Orbit.ViewModels;
using Cursors = System.Windows.Input.Cursors;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace Orbit.Views;

public partial class ConstellationBoardView : System.Windows.Controls.UserControl
{
    private Point? _panAnchor;
    private double _panOriginHorizontal;
    private double _panOriginVertical;

    public ConstellationBoardView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private ConstellationBoardViewModel? ViewModel => DataContext as ConstellationBoardViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        CenterCanvas();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        ReleaseMouseCapture();
        _panAnchor = null;
    }

    private void CenterCanvas()
    {
        if (ViewModel == null)
        {
            return;
        }

        var centerX = Math.Max(0, (ViewModel.CanvasCenterX * ViewModel.Zoom) - (BoardScroller.ViewportWidth / 2.0));
        var centerY = Math.Max(0, (ViewModel.CanvasCenterY * ViewModel.Zoom) - (BoardScroller.ViewportHeight / 2.0));

        BoardScroller.ScrollToHorizontalOffset(centerX);
        BoardScroller.ScrollToVerticalOffset(centerY);
    }

    private void BoardScroller_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panAnchor = e.GetPosition(BoardScroller);
        _panOriginHorizontal = BoardScroller.HorizontalOffset;
        _panOriginVertical = BoardScroller.VerticalOffset;
        BoardScroller.CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void BoardScroller_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_panAnchor == null || e.MiddleButton != MouseButtonState.Pressed)
        {
            return;
        }

        var current = e.GetPosition(BoardScroller);
        var delta = current - _panAnchor.Value;

        BoardScroller.ScrollToHorizontalOffset(Math.Max(0, _panOriginHorizontal - delta.X));
        BoardScroller.ScrollToVerticalOffset(Math.Max(0, _panOriginVertical - delta.Y));

        e.Handled = true;
    }

    private void BoardScroller_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        _panAnchor = null;
        BoardScroller.ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        e.Handled = true;
    }

    private void BoardScroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || ViewModel == null)
        {
            return;
        }

        var oldZoom = ViewModel.Zoom;
        var delta = e.Delta > 0 ? 0.1 : -0.1;
        ViewModel.NudgeZoom(delta);
        var newZoom = ViewModel.Zoom;

        if (Math.Abs(newZoom - oldZoom) < 0.0001)
        {
            return;
        }

        var cursor = e.GetPosition(BoardScroller);
        var logicalX = (BoardScroller.HorizontalOffset + cursor.X) / oldZoom;
        var logicalY = (BoardScroller.VerticalOffset + cursor.Y) / oldZoom;

        BoardScroller.ScrollToHorizontalOffset((logicalX * newZoom) - cursor.X);
        BoardScroller.ScrollToVerticalOffset((logicalY * newZoom) - cursor.Y);

        e.Handled = true;
    }

    private void SessionSun_MouseEnter(object sender, MouseEventArgs e)
    {
        if (ViewModel == null || sender is not FrameworkElement element || element.DataContext is not ConstellationSessionNodeViewModel node)
        {
            return;
        }

        ViewModel.ExpandNode(node, expanded: true);
    }

    private void SessionSun_MouseLeave(object sender, MouseEventArgs e)
    {
        if (ViewModel == null || sender is not FrameworkElement element || element.DataContext is not ConstellationSessionNodeViewModel node)
        {
            return;
        }

        ViewModel.ExpandNode(node, expanded: false);
    }

    private void SessionSun_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not FrameworkElement element || element.DataContext is not ConstellationSessionNodeViewModel node)
        {
            return;
        }

        ViewModel.FocusSession(node);
        ViewModel.TogglePinned(node);
    }

    private void ToolNode_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not System.Windows.Controls.Button button || button.CommandParameter is not ConstellationToolNodeViewModel tool)
        {
            return;
        }

        ViewModel.InvokeTool(tool);
        e.Handled = true;
    }
}
