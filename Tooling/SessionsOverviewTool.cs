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
    private readonly ScriptOrchestrationService _orchestration;
    private readonly SessionPlacementService? _placementService;

    public SessionsOverviewTool(
        SessionReconciliationService reconciliationService,
        OrbitLayoutStateService orbitLayoutState, ScriptOrchestrationService orchestration, SessionPlacementService? placementService = null)
    {
        _reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
        _orbitLayoutState = orbitLayoutState ?? throw new ArgumentNullException(nameof(orbitLayoutState));
        _orchestration = orchestration;
        _placementService = placementService;
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

        SessionsOverviewView? view = null;
        var originalOwner = new WeakReference<MainWindowViewModel>(mainVm);
        MainWindowViewModel Owner() => Window.GetWindow(view!)?.DataContext as MainWindowViewModel
            ?? (originalOwner.TryGetTarget(out var owner) ? owner : throw new InvalidOperationException("Session tool has no owner."));
        var viewModel = new SessionsOverviewViewModel(
            mainVm.Sessions,
            session => Owner().ActivateSession(session),
            session => Owner().FocusSession(session),
            session => Owner().CloseSession(session),
            session => Owner().ToggleNativeDebugMenuAsync(session),
            session => Owner().CanToggleNativeDebugMenu(session),
            _reconciliationService,
            _orbitLayoutState, _orchestration, _placementService);

        view = new SessionsOverviewView(viewModel);
        return view;
    }
}
