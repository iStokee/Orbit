using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class SessionsOverviewTool : IOrbitTool
{
    public string Key => "SessionsOverview";

    public string DisplayName => "Sessions";

    public PackIconMaterialKind Icon => PackIconMaterialKind.ViewList;

    public FrameworkElement CreateView(object? context = null)
    {
        if (context is not MainWindowViewModel mainVm)
        {
            throw new InvalidOperationException("SessionsOverviewTool requires MainWindowViewModel context.");
        }

        var viewModel = new SessionsOverviewViewModel(
            mainVm.Sessions,
            mainVm.ActivateSession,
            mainVm.FocusSession,
            mainVm.CloseSession);

        return new SessionsOverviewView(viewModel);
    }
}
