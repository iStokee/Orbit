using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class McpControlCenterView : UserControl
{
    private bool _disposed;

    public McpControlCenterView(McpControlCenterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_disposed)
        {
            return;
        }

        if (DataContext is System.IDisposable disposable)
        {
            disposable.Dispose();
        }

        _disposed = true;
        Unloaded -= OnUnloaded;
    }
}
