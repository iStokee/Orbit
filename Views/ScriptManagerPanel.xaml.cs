using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Orbit.Models;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class ScriptManagerPanel : UserControl
{
    public ScriptManagerPanel() : this(new ScriptManagerViewModel())
    {
    }

    public ScriptManagerPanel(ScriptManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnScriptDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Selector { SelectedItem: ScriptProfile profile })
        {
            return;
        }

        if (DataContext is ScriptManagerViewModel vm && vm.LoadScriptCommand.CanExecute(profile))
        {
            vm.LoadScriptCommand.Execute(profile);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Unloaded can fire during tab reparenting/tear-off; do not dispose view model here.
    }
}
