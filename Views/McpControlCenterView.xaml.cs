using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class McpControlCenterView : UserControl, Orbit.Services.IToolViewLifetime
{
    public McpControlCenterView(McpControlCenterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
    public void Dispose() => (DataContext as System.IDisposable)?.Dispose();
}