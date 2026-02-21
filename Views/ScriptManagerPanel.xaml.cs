using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Models;
using Orbit.ViewModels;
using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class ScriptManagerPanel : UserControl
{
    public ScriptManagerPanel() : this(ResolveViewModel())
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

    private static ScriptManagerViewModel ResolveViewModel()
    {
        var app = Application.Current as App;
        var viewModel = app?.Services.GetService<ScriptManagerViewModel>();
        if (viewModel == null)
        {
            throw new InvalidOperationException("ScriptManagerPanel requires ScriptManagerViewModel from DI.");
        }

        return viewModel;
    }
}
