using System;
using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class McpControlCenterView : UserControl
{
    public McpControlCenterView(McpControlCenterViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is IDisposable disposable)
        {
            disposable.Dispose();
        }

        Unloaded -= OnUnloaded;
    }
}
