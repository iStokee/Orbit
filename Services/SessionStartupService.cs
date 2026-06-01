using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Orbit.Models;
using Orbit.Views;

namespace Orbit.Services;

public sealed class SessionStartupService
{
	private readonly SessionManagerService sessionManager;
	private readonly SessionNameService sessionNameService;
	private readonly SessionPlacementService sessionPlacementService;
	private readonly SessionUiCoordinatorService sessionUiCoordinator;
	private readonly SemaphoreSlim orbitSessionLaunchGate = new(1, 1);

	public SessionStartupService(
		SessionManagerService sessionManager,
		SessionNameService sessionNameService,
		SessionPlacementService sessionPlacementService,
		SessionUiCoordinatorService sessionUiCoordinator)
	{
		this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
		this.sessionNameService = sessionNameService ?? throw new ArgumentNullException(nameof(sessionNameService));
		this.sessionPlacementService = sessionPlacementService ?? throw new ArgumentNullException(nameof(sessionPlacementService));
		this.sessionUiCoordinator = sessionUiCoordinator ?? throw new ArgumentNullException(nameof(sessionUiCoordinator));
	}

	public async Task<bool> AddSingleSessionAsync(
		ObservableCollection<SessionModel> sessions,
		ObservableCollection<object> tabs,
		string? preferredName,
		bool autoInjectOnReady,
		Action<object> handleTabRemoval,
		object orbitContext,
		Action<SessionModel> selectSession,
		Action<object> selectTab,
		Action openOrbitView)
	{
		var launchBehavior = SessionLaunchCoordinatorService.NormalizeSessionLaunchBehavior(Settings.Default.SessionLaunchBehavior);
		var gateHeld = false;

		if (string.Equals(launchBehavior, "OrbitView", StringComparison.Ordinal))
		{
			await orbitSessionLaunchGate.WaitAsync().ConfigureAwait(true);
			gateHeld = true;
		}

		var hostControl = new ChildClientView();
		var initialized = false;

		var resolvedName = sessionNameService.ResolveSessionName(sessions, preferredName);
		var session = new SessionModel
		{
			Id = Guid.NewGuid(),
			Name = resolvedName,
			CreatedAt = DateTime.Now,
			HostControl = hostControl,
			RequireInjectionBeforeDock = autoInjectOnReady
		};

		hostControl.DataContext = session;
		sessions.Add(session);

		if (string.Equals(launchBehavior, "OrbitView", StringComparison.Ordinal))
		{
			sessionUiCoordinator.MoveItemToOrbit(session, handleTabRemoval, orbitContext, "launch-session-to-orbit");
			selectSession(session);
			if (!tabs.OfType<ToolTabItem>().Any(t => string.Equals(t.Key, ShellPresentationPolicyService.OrbitViewToolKey, StringComparison.Ordinal)))
			{
				openOrbitView();
			}
		}
		else
		{
			sessionPlacementService.SetPlacement(session, SessionPlacementKind.MainTabs);
			tabs.Add(session);
			selectSession(session);
			selectTab(session);
		}

		try
		{
			await sessionManager.InitializeSessionAsync(session).ConfigureAwait(true);
			initialized = true;

			if (autoInjectOnReady && session.InjectionState == InjectionState.Ready)
			{
				Console.WriteLine($"[Orbit] Session '{session.Name}' is ready, auto-injecting...");
				await sessionManager.InjectAsync(session).ConfigureAwait(true);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[Orbit] Session initialization/injection failed: {ex.Message}");
		}
		finally
		{
			if (gateHeld)
			{
				orbitSessionLaunchGate.Release();
			}

			CommandManager.InvalidateRequerySuggested();
		}

		return initialized;
	}
}
