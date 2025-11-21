using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Orbit.Models;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class FsmNodeEditorView : UserControl
{
	public FsmNodeEditorView()
	{
		InitializeComponent();
	}

	private void NodeMouseDown(object sender, MouseButtonEventArgs e)
	{
		if (DataContext is not FsmNodeEditorViewModel vm)
			return;

		if (sender is Thumb thumb && thumb.Tag is FsmNodeModel node)
		{
			vm.SelectedNode = node;
			e.Handled = true;
		}
	}

	private void NodeDragDelta(object sender, DragDeltaEventArgs e)
	{
		if (sender is not Thumb thumb || thumb.Tag is not FsmNodeModel node)
			return;

		node.X = System.Math.Max(0, node.X + e.HorizontalChange);
		node.Y = System.Math.Max(0, node.Y + e.VerticalChange);
	}
}
