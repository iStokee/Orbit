using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Orbit.ViewModels;
using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class ThemeManagerPanel : UserControl
{
    public ThemeManagerPanel() : this(ResolveViewModel())
    {
    }

    public ThemeManagerPanel(ThemeManagerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
    }

    private static ThemeManagerViewModel ResolveViewModel()
    {
        var app = Application.Current as App;
        var viewModel = app?.Services.GetService<ThemeManagerViewModel>();
        if (viewModel == null)
        {
            throw new InvalidOperationException("ThemeManagerPanel requires ThemeManagerViewModel from DI.");
        }

        return viewModel;
    }
}
