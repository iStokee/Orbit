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
	internal static string? ResolvePlacementChoice(System.Windows.MessageBoxResult choice) => choice switch
	{
		System.Windows.MessageBoxResult.Yes => "OrbitView",
		System.Windows.MessageBoxResult.No => "IndividualTabs",
		_ => null
	};

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
		Action openOrbitView,
		string? launcherCharacterId = null,
		Func<bool>? canLaunch = null)
	{
		if (System.Windows.Application.Current is App app && app.IsShuttingDown) return false;
		if (canLaunch != null && !canLaunch()) return false;
		var launchBehavior = SessionLaunchCoordinatorService.NormalizeSessionLaunchBehavior(Settings.Default.SessionLaunchBehavior);
		if (string.Equals(launchBehavior, "Ask", StringComparison.Ordinal))
		{
			var choice = System.Windows.MessageBox.Show("Launch this session in Orbit View?\n\nYes: Orbit View\nNo: Individual tab\nCancel: Do not launch", "Session placement",
				System.Windows.MessageBoxButton.YesNoCancel, System.Windows.MessageBoxImage.Question);
			var selectedPlacement = ResolvePlacementChoice(choice);
			if (selectedPlacement == null) return false;
			launchBehavior = selectedPlacement;
		}
		var gateHeld = false;

		if (string.Equals(launchBehavior, "OrbitView", StringComparison.Ordinal))
		{
			await orbitSessionLaunchGate.WaitAsync().ConfigureAwait(true);
			gateHeld = true;
		}

		try
		{
			if (System.Windows.Application.Current is App currentApp && currentApp.IsShuttingDown) return false;
			if (canLaunch != null && !canLaunch()) return false;
			var hostControl = new ChildClientView();
			var initialized = false;

			var resolvedName = sessionNameService.ResolveSessionName(sessions, preferredName);
			var session = new SessionModel
			{
				Id = Guid.NewGuid(),
				Name = resolvedName,
				CreatedAt = DateTime.Now,
				HostControl = hostControl,
				RequireInjectionBeforeDock = autoInjectOnReady,
				LauncherCharacterId = launcherCharacterId
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
				if (canLaunch != null && !canLaunch()) return false;
				if (autoInjectOnReady && session.InjectionState == InjectionState.Ready)
				{
					Console.WriteLine($"[Orbit] Session '{session.Name}' is ready, auto-injecting...");
					await sessionManager.InjectAsync(session).ConfigureAwait(true);
				}
				initialized = true;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Orbit] Session initialization/injection failed: {ex.Message}");
			}
			finally
			{
				// Resolution failures can occur before InjectAsync enters its own finally.
				session.RSForm?.SignalInjectionReady();
			}
			try
			{
				await sessionManager.CompleteDockingAsync(session).ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				initialized = false;
				if (session.State is not (SessionState.Closed or SessionState.ShuttingDown)) session.Fail(ex);
				Console.WriteLine($"[Orbit] Session docking failed: {ex.Message}");
			}
			return initialized;
		}
		finally
		{
			if (gateHeld)
			{
				orbitSessionLaunchGate.Release();
			}

			CommandManager.InvalidateRequerySuggested();
		}

	}
}
