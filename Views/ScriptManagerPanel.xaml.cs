using System.Windows.Controls;
using System.Windows.Input;
using Orbit.Models;
using Orbit.ViewModels;
using ListView = System.Windows.Controls.ListView;
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
        if (sender is not ListView { SelectedItem: ScriptProfile profile })
        {
            return;
        }

        if (DataContext is ScriptManagerViewModel vm && vm.LoadScriptCommand.CanExecute(profile))
        {
            vm.LoadScriptCommand.Execute(profile);
        }
    }
}
