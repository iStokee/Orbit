using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class SessionsOverviewView : UserControl
{
    public SessionsOverviewView(SessionsOverviewViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        // Unloaded can fire during tab reparenting/tear-off; do not dispose view model here.
        Unloaded -= OnUnloaded;
    }
}
