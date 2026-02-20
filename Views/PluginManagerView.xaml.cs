using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class PluginManagerView : UserControl
{
    public PluginManagerView()
    {
        InitializeComponent();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unloaded can fire during tab reparenting/tear-off; do not dispose view model here.
    }
}
