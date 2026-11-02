using System;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Orbit.ViewModels;
using Application = System.Windows.Application;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;

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
        var viewModel = app?.Services.GetRequiredService<Orbit.Services.OwnedViewFactory>().CreateModel<ThemeManagerViewModel>();
        if (viewModel == null)
        {
            throw new InvalidOperationException("ThemeManagerPanel requires ThemeManagerViewModel from DI.");
        }

        return viewModel;
    }

    private void OnThemeLayoutSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var stacked = ThemeLayout.ActualWidth < 940;
        Grid.SetColumnSpan(ThemeBuilderCard, stacked ? 2 : 1);
        Grid.SetRow(ThemePreviewAndLibrary, stacked ? 1 : 0);
        Grid.SetColumn(ThemePreviewAndLibrary, stacked ? 0 : 1);
        Grid.SetColumnSpan(ThemePreviewAndLibrary, stacked ? 2 : 1);
        ThemeBuilderCard.Margin = stacked ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 10, 0);
        if (stacked)
        {
            BindingOperations.ClearBinding(ThemePreviewAndLibrary, MaxHeightProperty);
            ThemePreviewAndLibrary.MaxHeight = double.PositiveInfinity;
        }
        else
        {
            ThemePreviewAndLibrary.SetBinding(MaxHeightProperty,
                new Binding(nameof(ActualHeight)) { Source = ThemeBuilderCard });
        }
    }
}
