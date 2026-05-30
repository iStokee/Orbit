using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Services;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class SessionsOverviewTool : IOrbitTool
{
    private readonly SessionReconciliationService _reconciliationService;
    private readonly OrbitLayoutStateService _orbitLayoutState;

    public SessionsOverviewTool(
        SessionReconciliationService reconciliationService,
        OrbitLayoutStateService orbitLayoutState)
    {
        _reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
        _orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));
    }

    public string Key => "SessionsOverview";

    public string DisplayName => "Sessions";

    public PackIconMaterialKind Icon => PackIconMaterialKind.MonitorDashboard;

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
            mainVm.CloseSession,
            session => mainVm.ToggleNativeDebugMenuAsync(session),
            session => mainVm.CanToggleNativeDebugMenu(session),
            _reconciliationService,
            _orbitLayoutState);

        return new SessionsOverviewView(viewModel);
    }
}
