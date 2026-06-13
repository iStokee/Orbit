using Dragablz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Orbit.Tooling;
using Orbit;
using Orbit.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MahApps.Metro.IconPacks;
using MahApps.Metro.Controls.Dialogs;
using MahApps.Metro.Controls;
using FlowDirection = System.Windows.FlowDirection;

namespace Orbit.ViewModels
{
	/// <summary>
	/// Primary coordinator for the Orbit shell window. Owns the session collection, orchestrates tool tabs,
	/// and keeps global state (floating menu, hot reload targets, etc.) in sync across detached windows.
	/// </summary>
	public class MainWindowViewModel : INotifyPropertyChanged, IDisposable
	{
	private readonly SessionManagerService sessionManager;
	private readonly ThemeService themeService;
	private readonly ScriptIntegrationService scriptIntegrationService;
	private readonly ScriptManagerService scriptManagerService;
	private readonly ScriptOrchestrationService scriptOrchestrationService;
	private readonly SessionCollectionService sessionCollectionService;
	private readonly SessionAutoRelaunchService sessionAutoRelaunchService;
	private readonly SessionLaunchCoordinatorService sessionLaunchCoordinator;
	private readonly SessionLifecycleCoordinatorService sessionLifecycleCoordinator;
	private readonly SessionPlacementService sessionPlacementService;
	private readonly SessionRenameService sessionRenameService;
	private readonly SessionReconciliationService sessionReconciliationService;
	private readonly SessionStartupService sessionStartupService;
	private readonly SessionTargetResolverService sessionTargetResolver;
	private readonly SessionUiCoordinatorService sessionUiCoordinator;
	private readonly ShellPresentationPolicyService shellPresentationPolicyService;
	private readonly ShellSessionCloseService shellSessionCloseService;
	private readonly ShellSessionFocusService shellSessionFocusService;
	private readonly ShellSessionRecoveryService shellSessionRecoveryService;
	private readonly ShellSessionSelectionService shellSessionSelectionService;
	private readonly ShellTabCollectionCoordinatorService shellTabCollectionCoordinatorService;
	private readonly ShellTabSelectionService shellTabSelectionService;
	private readonly ShellToolCoordinatorService shellToolCoordinatorService;
	private readonly OrbitLayoutStateService orbitLayoutState;
	private readonly ConsoleLogService consoleLogService;
	private readonly FloatingMenuGeometryService floatingMenuGeometryService;
	private readonly FloatingMenuVisibilityService floatingMenuVisibilityService;
	private readonly InterTabClient interTabClient;
	private readonly MesharpHotkeyService mesharpHotkeyService;
	private readonly MesharpSessionCommandService mesharpSessionCommandService;
	private readonly SettingsViewModel settingsViewModel;
	private SessionModel selectedSession;
	private SessionModel? hotReloadTargetSession;
	private string hotReloadScriptPath;
	private ScriptProfile selectedScript;
	private double floatingMenuLeft;
	private double floatingMenuTop;
	private double floatingMenuOpacity;
	private double floatingMenuBackgroundOpacity;
	private bool isFloatingMenuVisible;
	private bool isFloatingMenuExpanded;
	private bool isFloatingMenuWelcomeVisible;
	private FloatingMenuDirection floatingMenuDirection;
	private FloatingMenuDirection autoActiveSide;
	private double floatingMenuInactivitySeconds;
	private bool floatingMenuAutoDirection;
	// Floating menu docking overlay hints and viewport tracking
	private bool isFloatingMenuDockOverlayVisible;
	private FloatingMenuDockRegion floatingMenuDockCandidate = FloatingMenuDockRegion.None;
	public double hostViewportWidth = 1200;
	public double hostViewportHeight = 900;
	private bool autoInjectOnReady;
	private bool isFloatingMenuClipping;
	private readonly DispatcherTimer sessionCrashWatchTimer;
	private bool isApplicationShuttingDown;
	private bool hasUpdateNotification;
	private bool isFloatingMenuDragging;

	/// <summary>
	/// Creates the main window view model. Most dependencies are long-lived services shared via DI;
	/// the constructor wires them so detached windows can reuse the same singletons.
	/// </summary>
	public MainWindowViewModel(
		SessionManagerService sessionManager,
		ThemeService themeService,
		ScriptIntegrationService scriptIntegrationService,
		ScriptManagerService scriptManagerService,
		ScriptOrchestrationService scriptOrchestrationService,
		SessionCollectionService sessionCollectionService,
		SessionAutoRelaunchService sessionAutoRelaunchService,
		SessionLaunchCoordinatorService sessionLaunchCoordinator,
		SessionLifecycleCoordinatorService sessionLifecycleCoordinator,
		SessionPlacementService sessionPlacementService,
		SessionRenameService sessionRenameService,
		SessionReconciliationService sessionReconciliationService,
		SessionStartupService sessionStartupService,
		SessionTargetResolverService sessionTargetResolver,
		SessionUiCoordinatorService sessionUiCoordinator,
		ShellPresentationPolicyService shellPresentationPolicyService,
		ShellSessionCloseService shellSessionCloseService,
		ShellSessionFocusService shellSessionFocusService,
		ShellSessionRecoveryService shellSessionRecoveryService,
		ShellSessionSelectionService shellSessionSelectionService,
		ShellTabCollectionCoordinatorService shellTabCollectionCoordinatorService,
		ShellTabSelectionService shellTabSelectionService,
		ShellToolCoordinatorService shellToolCoordinatorService,
		OrbitLayoutStateService orbitLayoutStateService,
		ConsoleLogService consoleLogService,
		FloatingMenuGeometryService floatingMenuGeometryService,
		FloatingMenuVisibilityService floatingMenuVisibilityService,
		InterTabClient interTabClient,
		MesharpHotkeyService mesharpHotkeyService,
		MesharpSessionCommandService mesharpSessionCommandService,
		SettingsViewModel settingsViewModel)
	{
		this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
		this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
		this.scriptIntegrationService = scriptIntegrationService ?? throw new ArgumentNullException(nameof(scriptIntegrationService));
		this.scriptManagerService = scriptManagerService ?? throw new ArgumentNullException(nameof(scriptManagerService));
		this.scriptOrchestrationService = scriptOrchestrationService ?? throw new ArgumentNullException(nameof(scriptOrchestrationService));
		this.sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
		this.sessionAutoRelaunchService = sessionAutoRelaunchService ?? throw new ArgumentNullException(nameof(sessionAutoRelaunchService));
		this.sessionLaunchCoordinator = sessionLaunchCoordinator ?? throw new ArgumentNullException(nameof(sessionLaunchCoordinator));
		this.sessionLifecycleCoordinator = sessionLifecycleCoordinator ?? throw new ArgumentNullException(nameof(sessionLifecycleCoordinator));
		this.sessionPlacementService = sessionPlacementService ?? throw new ArgumentNullException(nameof(sessionPlacementService));
		this.sessionRenameService = sessionRenameService ?? throw new ArgumentNullException(nameof(sessionRenameService));
		this.sessionReconciliationService = sessionReconciliationService ?? throw new ArgumentNullException(nameof(sessionReconciliationService));
		this.sessionStartupService = sessionStartupService ?? throw new ArgumentNullException(nameof(sessionStartupService));
		this.sessionTargetResolver = sessionTargetResolver ?? throw new ArgumentNullException(nameof(sessionTargetResolver));
		this.sessionUiCoordinator = sessionUiCoordinator ?? throw new ArgumentNullException(nameof(sessionUiCoordinator));
		this.shellPresentationPolicyService = shellPresentationPolicyService ?? throw new ArgumentNullException(nameof(shellPresentationPolicyService));
		this.shellSessionCloseService = shellSessionCloseService ?? throw new ArgumentNullException(nameof(shellSessionCloseService));
		this.shellSessionFocusService = shellSessionFocusService ?? throw new ArgumentNullException(nameof(shellSessionFocusService));
		this.shellSessionRecoveryService = shellSessionRecoveryService ?? throw new ArgumentNullException(nameof(shellSessionRecoveryService));
		this.shellSessionSelectionService = shellSessionSelectionService ?? throw new ArgumentNullException(nameof(shellSessionSelectionService));
		this.shellTabCollectionCoordinatorService = shellTabCollectionCoordinatorService ?? throw new ArgumentNullException(nameof(shellTabCollectionCoordinatorService));
		this.shellTabSelectionService = shellTabSelectionService ?? throw new ArgumentNullException(nameof(shellTabSelectionService));
		this.shellToolCoordinatorService = shellToolCoordinatorService ?? throw new ArgumentNullException(nameof(shellToolCoordinatorService));
			this.orbitLayoutState = orbitLayoutStateService ?? throw new ArgumentNullException(nameof(orbitLayoutStateService));
			this.consoleLogService = consoleLogService ?? throw new ArgumentNullException(nameof(consoleLogService));
			this.floatingMenuGeometryService = floatingMenuGeometryService ?? throw new ArgumentNullException(nameof(floatingMenuGeometryService));
			this.floatingMenuVisibilityService = floatingMenuVisibilityService ?? throw new ArgumentNullException(nameof(floatingMenuVisibilityService));
			this.interTabClient = interTabClient ?? throw new ArgumentNullException(nameof(interTabClient));
			this.mesharpHotkeyService = mesharpHotkeyService ?? throw new ArgumentNullException(nameof(mesharpHotkeyService));
			this.mesharpSessionCommandService = mesharpSessionCommandService ?? throw new ArgumentNullException(nameof(mesharpSessionCommandService));
			this.settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

		ScriptManager = this.scriptManagerService;
		ConsoleLog = this.consoleLogService;

		Sessions = this.sessionCollectionService.Sessions;
		Tabs = new ObservableCollection<object>();
		Tabs.CollectionChanged += OnTabsCollectionChanged;
		Sessions.CollectionChanged += OnSessionsCollectionChanged;
		foreach (var session in Sessions)
		{
			session.PropertyChanged += OnSessionPropertyChanged;
		}

		selectedSession = this.sessionCollectionService.GlobalSelectedSession;
		this.sessionCollectionService.PropertyChanged += OnGlobalSessionChanged;

		InterTabClient = this.interTabClient;
		hotReloadScriptPath = Settings.Default.HotReloadScriptPath ?? string.Empty;

		AddSessionCommand = new AsyncRelayCommand(AddSessionAsync);
		InjectCommand = new AsyncRelayCommand(InjectAsync, CanInject);
		ShowSessionsCommand = new RelayCommand(ShowSessions, () => Sessions.Count > 0);
		OpenSessionGalleryCommand = new RelayCommand(() => TryOpenToolByKey(ShellToolCoordinatorService.SessionGalleryToolKey));
	OpenOrbitViewCommand = new RelayCommand(OpenOrbitViewWorkspace);
	MoveTabToOrbitCommand = new RelayCommand<object?>(MoveTabToOrbit, CanMoveTabToOrbit);
	MoveSessionToIndividualTabsCommand = new RelayCommand<object?>(MoveSessionToIndividualTabs, CanMoveSessionToIndividualTabs);
	OpenThemeManagerCommand = new RelayCommand(OpenThemeManager);
		OpenScriptManagerCommand = new RelayCommand(OpenScriptManager);
		OpenSharpBuilderCommand = new RelayCommand(OpenSharpBuilder);
		OpenAccountManagerCommand = new RelayCommand(OpenAccountManager, () => shellToolCoordinatorService.IsToolAvailable(ShellToolCoordinatorService.AccountManagerToolKey));
		OpenGuideCommand = new RelayCommand(OpenGuideTab);
		OpenMESharpApiBrowserCommand = new RelayCommand(OpenMESharpApiBrowserTab);
		OpenSettingsCommand = new RelayCommand(OpenSettingsTab);
		OpenToolsOverviewCommand = new RelayCommand(OpenToolsOverviewTab);
		OpenMcpControlCommand = new RelayCommand(OpenMcpControlTab);
		ToggleConsoleCommand = new RelayCommand(ToggleConsole);
		BrowseScriptCommand = new RelayCommand(BrowseForScript);
		LoadScriptCommand = new RelayCommand(async () => await LoadScriptAsync(), CanLoadScript);
		ReloadScriptCommand = new RelayCommand(async () => await ReloadScriptAsync(), CanReloadScript);
		BeginSessionRenameCommand = new RelayCommand<object?>(BeginSessionRename, parameter => parameter is SessionModel);
		CommitSessionRenameCommand = new RelayCommand<object?>(CommitSessionRename, parameter => parameter is SessionModel);
		CancelSessionRenameCommand = new RelayCommand<object?>(CancelSessionRename, parameter => parameter is SessionModel);
		FloatingMenuWelcomeCommand = new RelayCommand(OnFloatingMenuWelcomeInvoked);

		floatingMenuLeft = Settings.Default.FloatingMenuLeft;
		floatingMenuTop = Settings.Default.FloatingMenuTop;
		floatingMenuOpacity = Settings.Default.FloatingMenuOpacity <= 0
			? 0.95
			: Settings.Default.FloatingMenuOpacity;
		floatingMenuDirection = Enum.TryParse(Settings.Default.FloatingMenuDirection, out FloatingMenuDirection storedDirection)
			? storedDirection
			: FloatingMenuDirection.Right;
		floatingMenuBackgroundOpacity = Settings.Default.FloatingMenuBackgroundOpacity <= 0
			? 0.9
			: Settings.Default.FloatingMenuBackgroundOpacity;
		floatingMenuInactivitySeconds = Settings.Default.FloatingMenuInactivitySeconds <= 0 ? 2 : Settings.Default.FloatingMenuInactivitySeconds;
		floatingMenuAutoDirection = Settings.Default.FloatingMenuAutoDirection;
		isFloatingMenuVisible = false; // Will be updated when first tab is selected
		FloatingMenuDirectionOptions = Enum.GetValues(typeof(FloatingMenuDirection));
		FloatingMenuQuickToggleModes = Enum.GetValues(typeof(FloatingMenuQuickToggleMode));
		autoInjectOnReady = Settings.Default.AutoInjectOnReady;

		// initialize auto side from current position
		ComputeAutoActiveSide();
		var initialHotReloadTarget = this.sessionCollectionService.GlobalHotReloadTargetSession
			?? selectedSession
			?? Sessions.FirstOrDefault();
		HotReloadTargetSession = initialHotReloadTarget;

			ApplyThemeFromSettings();
			UpdateFloatingMenuWelcomeHint(Settings.Default.ShowThemeManagerWelcomeMessage);
			hasUpdateNotification = this.settingsViewModel.HasUpdate;
			this.settingsViewModel.PropertyChanged += OnSettingsViewModelPropertyChanged;

			sessionCrashWatchTimer = new DispatcherTimer(DispatcherPriority.Background)
			{
				Interval = TimeSpan.FromSeconds(2)
			};
			sessionCrashWatchTimer.Tick += SessionCrashWatchTimer_Tick;
			sessionCrashWatchTimer.Start();
		}

	public ObservableCollection<SessionModel> Sessions { get; }
	public ObservableCollection<object> Tabs { get; }
	public IInterTabClient InterTabClient { get; }
	public bool HasSessions => Sessions.Count > 0;
	public ScriptManagerService ScriptManager { get; }
	public ScriptIntegrationService ScriptIntegration => scriptIntegrationService;
	public IAsyncRelayCommand AddSessionCommand { get; }
	public IAsyncRelayCommand InjectCommand { get; }
	public IRelayCommand ShowSessionsCommand { get; }
	public IRelayCommand OpenSessionGalleryCommand { get; }
	public IRelayCommand OpenOrbitViewCommand { get; }
	public IRelayCommand<object?> MoveTabToOrbitCommand { get; }
	public IRelayCommand<object?> MoveSessionToIndividualTabsCommand { get; }
	public IRelayCommand OpenThemeManagerCommand { get; }
	public IRelayCommand OpenScriptManagerCommand { get; }
	public IRelayCommand OpenSharpBuilderCommand { get; }
	public IRelayCommand ToggleConsoleCommand { get; }
	public IRelayCommand BrowseScriptCommand { get; }
	public IRelayCommand LoadScriptCommand { get; }
	public IRelayCommand ReloadScriptCommand { get; }
	public IRelayCommand<object?> BeginSessionRenameCommand { get; }
	public IRelayCommand<object?> CommitSessionRenameCommand { get; }
	public IRelayCommand<object?> CancelSessionRenameCommand { get; }
	public IRelayCommand FloatingMenuWelcomeCommand { get; }
	public ConsoleLogService ConsoleLog { get; }
	public IRelayCommand OpenAccountManagerCommand { get; }
	public IRelayCommand OpenGuideCommand { get; }
	public IRelayCommand OpenMESharpApiBrowserCommand { get; }
	public IRelayCommand OpenSettingsCommand { get; }
	public IRelayCommand OpenToolsOverviewCommand { get; }
	public IRelayCommand OpenMcpControlCommand { get; }
	public Array FloatingMenuDirectionOptions { get; }
	public Array FloatingMenuQuickToggleModes { get; }
	public bool HasUpdateNotification
	{
		get => hasUpdateNotification;
		private set
		{
			if (hasUpdateNotification == value)
			{
				return;
			}

			hasUpdateNotification = value;
			OnPropertyChanged(nameof(HasUpdateNotification));
		}
	}

	public bool AutoInjectOnReady
	{
		get => autoInjectOnReady;
		set
		{
			if (autoInjectOnReady == value) return;
			autoInjectOnReady = value;
			Settings.Default.AutoInjectOnReady = value;
			OnPropertyChanged(nameof(AutoInjectOnReady));
			OnPropertyChanged(nameof(ShowInjectMenuButton));

			foreach (var session in Sessions)
			{
				session.RequireInjectionBeforeDock = value;
				var rsForm = session.RSForm;
				if (rsForm != null)
				{
					rsForm.WaitForInjectionBeforeDock = value;
					if (!value)
					{
						rsForm.SignalInjectionReady();
					}
				}
			}
		}
	}

		private object selectedTab;

		/// <summary>
		/// Currently selected tab in the shell. When a session tab is chosen we update <see cref="SelectedSession"/>
		/// and focus the embedded client; when a tool tab is selected we keep the previous session targeted so that
		/// script controls retain context.
		/// </summary>
        public object SelectedTab
        {
            get => selectedTab;
            set
            {
                if (ReferenceEquals(selectedTab, value)) return;
                selectedTab = value;

                // If a session tab is selected, update SelectedSession.
                // Otherwise (tool tab), keep the previous SelectedSession so script actions retain context.
                if (value is SessionModel sm)
                {
                    SelectedSession = sm;
                    try
                    {
						sm.HostControl?.EnsureActiveAfterLayout();
                        // Focus the embedded client when switching to a session tab
                        Application.Current.Dispatcher.InvokeAsync(() => sm.HostControl?.FocusEmbeddedClient());
                    }
                    catch { /* best effort */ }
                }

                // Update floating menu visibility based on tab type
                UpdateFloatingMenuVisibilityForCurrentTab();

                OnPropertyChanged(nameof(SelectedTab));
            }
        }

	/// <summary>
	/// Session currently driving the shell. Updates global selection services and ensures the floating menu
	/// targets the active client.
	/// </summary>
	public SessionModel SelectedSession
	{
		get => selectedSession;
		set
		{
			if (selectedSession == value)
				return;

			if (selectedSession != null)
			{
				selectedSession.PropertyChanged -= OnSelectedSessionChanged;
			}

			selectedSession = value;

			if (selectedSession != null)
			{
				selectedSession.PropertyChanged += OnSelectedSessionChanged;
			}

			// Sync with global selected session so tear-off windows can access it
			sessionCollectionService.GlobalSelectedSession = value;

			var resolvedHotReloadTarget = shellSessionSelectionService.ResolveHotReloadTargetAfterSelectionChanged(
				Sessions,
				value,
				hotReloadTargetSession);
			if (!ReferenceEquals(hotReloadTargetSession, resolvedHotReloadTarget))
			{
				HotReloadTargetSession = resolvedHotReloadTarget;
			}

			OnPropertyChanged(nameof(SelectedSession));
			CommandManager.InvalidateRequerySuggested();
		}
	}

		// Theme Logging Properties
		public bool IsThemeLoggingEnabled
		{
			get => ThemeLogger.IsEnabled;
			set
			{
				if (ThemeLogger.IsEnabled != value)
				{
					ThemeLogger.IsEnabled = value;
					OnPropertyChanged(nameof(IsThemeLoggingEnabled));
				}
			}
		}

		public string ThemeLogFilePath => ThemeLogger.LogFilePath;

		public IRelayCommand OpenThemeLogCommand => new RelayCommand(() =>
		{
			try
			{
				if (File.Exists(ThemeLogger.LogFilePath))
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
					{
						FileName = ThemeLogger.LogFilePath,
						UseShellExecute = true
					});
				}
				else
				{
					MessageBox.Show("Log file does not exist yet. Enable logging and apply a theme first.",
						"Log File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show($"Failed to open log file: {ex.Message}",
					"Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		});

		public IRelayCommand ClearThemeLogCommand => new RelayCommand(() =>
		{
			ThemeLogger.ClearLog();
			MessageBox.Show("Theme log cleared successfully.",
				"Log Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
		});

	public event PropertyChangedEventHandler PropertyChanged;

		public string HotReloadScriptPath
		{
			get => hotReloadScriptPath;
			set
			{
				var normalized = value ?? string.Empty;
				if (hotReloadScriptPath == normalized)
					return;

				hotReloadScriptPath = normalized;
				Settings.Default.HotReloadScriptPath = hotReloadScriptPath;
			OnPropertyChanged(nameof(HotReloadScriptPath));
			CommandManager.InvalidateRequerySuggested();
		}
	}

		/// <summary>
		/// Creates and initializes a new RuneScape session. Depending on user preference the session is either
		/// kept inside the Orbit view grid or materialized as its own tab.
		/// </summary>
		private async Task AddSessionAsync()
		{
			await sessionLaunchCoordinator.AddSessionAsync(
				() => AddSingleSessionAsync(),
				CommandManager.InvalidateRequerySuggested).ConfigureAwait(true);
		}

		private async Task<bool> AddSingleSessionAsync(string? preferredName = null)
		{
			return await sessionStartupService.AddSingleSessionAsync(
				Sessions,
				Tabs,
				preferredName,
				AutoInjectOnReady,
				HandleTabRemoval,
				this,
				session => SelectedSession = session,
				tab => SelectedTab = tab,
				() => OpenOrbitViewCommand?.Execute(null)).ConfigureAwait(true);
		}

		private async Task InjectAsync()
		{
			var session = SelectedSession;
			if (session == null)
			{
				return;
			}

			try
			{
				await InjectSessionInternalAsync(session);
			}
			catch (Exception)
			{
				// Failure is recorded on the model; swallow to keep UI responsive.
			}
			finally
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}

		private async Task InjectSessionInternalAsync(SessionModel session)
		{
			await sessionManager.InjectAsync(session);
		}

        private void ShowSessions()
        {
            // Open sessions overview as a tab
            OpenSessionsOverviewTab();
        }

        private void OpenThemeManager()
        {
            // Open theme manager as a tab
            OpenThemeManagerTab();
        }

	private void OpenScriptManager()
	{
		shellToolCoordinatorService.OpenScriptManager(Tabs, this, tab => SelectedTab = tab);
	}

	private void OpenSharpBuilder()
	{
		shellToolCoordinatorService.OpenSharpBuilder(Tabs, this, tab => SelectedTab = tab);
	}

		private void OpenAccountManager()
		{
			OpenAccountManagerTab();
		}

		public ScriptProfile SelectedScript
		{
			get => selectedScript;
			set
			{
				if (selectedScript == value)
					return;

				selectedScript = value;
				OnPropertyChanged(nameof(SelectedScript));

				if (selectedScript != null)
					HotReloadScriptPath = selectedScript.FilePath;
			}
		}

	public SessionModel? HotReloadTargetSession
	{
		get => hotReloadTargetSession;
		set
		{
				if (ReferenceEquals(hotReloadTargetSession, value))
					return;

				hotReloadTargetSession = value;
				sessionCollectionService.GlobalHotReloadTargetSession = value;
				OnPropertyChanged(nameof(HotReloadTargetSession));
				CommandManager.InvalidateRequerySuggested();
			}
		}

	/// <summary>
	/// Dragablz callback invoked when a tab close button is pressed. Session tabs route through the full shutdown
	/// pipeline while tool tabs simply disappear.
	/// </summary>
	public void TabControl_ClosingItemHandler(ItemActionCallbackArgs<TabablzControl> args)
			{
				var removedItem = ResolveClosingItem(args);
				if (removedItem is SessionModel session)
				{
					args.Cancel();
					if (sessionLifecycleCoordinator.IsCloseInProgress(session))
					{
						return;
					}
					_ = CloseSessionInternalAsync(session, skipConfirmation: false, forceKillOnTimeout: false);
					return;
				}

			if (removedItem is Models.ToolTabItem toolItem &&
				string.Equals(toolItem.Key, "OrbitView", StringComparison.Ordinal))
			{
				RestoreOrbitWorkspaceToTabs();
			}

			if (removedItem != null && Tabs.Contains(removedItem))
			{
				DisposeToolItem(removedItem);
				Tabs.Remove(removedItem);
				HandleTabRemoval(removedItem);
			}
		}

		private bool CanInject() => SelectedSession?.IsInjectable == true;

	private SessionModel? ResolveHotReloadTarget()
	{
		return sessionTargetResolver.ResolveHotReloadTarget(
			Sessions,
			sessionCollectionService.GlobalHotReloadTargetSession,
			hotReloadTargetSession,
			SelectedSession,
			sessionCollectionService.GlobalSelectedSession);
	}

	private bool CanLoadScript()
	{
		if (!Settings.Default.MesharpIntegrationEnabled)
		{
			return false;
		}

		var targetSession = ResolveHotReloadTarget();
		return targetSession?.InjectionState == InjectionState.Injected
			&& !string.IsNullOrWhiteSpace(hotReloadScriptPath);
	}

	private bool CanReloadScript() => CanLoadScript();

	private void BeginSessionRename(object parameter)
	{
		if (parameter is SessionModel session)
		{
			BeginSessionRename(session);
		}
	}

	public void BeginSessionRename(SessionModel session)
	{
		sessionRenameService.BeginRename(session);
	}

	private void CommitSessionRename(object parameter)
	{
		if (parameter is SessionModel session)
		{
			CommitSessionRename(session);
		}
	}

	public void CommitSessionRename(SessionModel session)
	{
		sessionRenameService.CommitRename(session);
	}

	private void CancelSessionRename(object parameter)
	{
		if (parameter is SessionModel session)
		{
			CancelSessionRename(session);
		}
	}

	public void CancelSessionRename(SessionModel session)
	{
		sessionRenameService.CancelRename(session);
	}

	public double FloatingMenuLeft
	{
		get => floatingMenuLeft;
		set
		{
			if (Math.Abs(floatingMenuLeft - value) < 0.1)
				return;
			floatingMenuLeft = value;
			Settings.Default.FloatingMenuLeft = value;
			OnPropertyChanged(nameof(FloatingMenuLeft));
		}
	}

	#region Tool Tabs

	public void OpenAccountManagerTab()
	{
		shellToolCoordinatorService.OpenAccountManager(Tabs, this, tab => SelectedTab = tab);
	}

    public void OpenSettingsTab()
    {
        shellToolCoordinatorService.OpenSettings(Tabs, this, tab => SelectedTab = tab);
    }

    public void OpenConsoleTab()
    {
        shellToolCoordinatorService.OpenConsole(Tabs, this, tab => SelectedTab = tab);
    }

    public void OpenThemeManagerTab()
    {
        shellToolCoordinatorService.OpenThemeManager(Tabs, this, tab => SelectedTab = tab);
    }

    public void OpenSessionsOverviewTab()
    {
        shellToolCoordinatorService.OpenSessionsOverview(Tabs, this, tab => SelectedTab = tab);
    }

	public void OpenGuideTab()
	{
		shellToolCoordinatorService.OpenGuide(Tabs, this, tab => SelectedTab = tab);
	}

	public void OpenMESharpApiBrowserTab()
	{
		shellToolCoordinatorService.OpenMESharpApiBrowser(Tabs, this, tab => SelectedTab = tab);
	}

	public void OpenToolsOverviewTab()
	{
		shellToolCoordinatorService.OpenToolsOverview(Tabs, this, tab => SelectedTab = tab);
	}

	public void OpenMcpControlTab()
	{
		shellToolCoordinatorService.OpenMcpControl(Tabs, this, tab => SelectedTab = tab);
	}

	private bool TryOpenToolByKey(string key)
	{
		return shellToolCoordinatorService.TryOpenToolByKey(Tabs, this, key, tab => SelectedTab = tab);
	}

	private void OpenOrbitViewWorkspace()
	{
		TryOpenToolByKey(ShellPresentationPolicyService.OrbitViewToolKey);
		AdoptSessionsIntoOrbitWorkspace();
	}

	private void OnSettingsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!string.Equals(e.PropertyName, nameof(SettingsViewModel.HasUpdate), StringComparison.Ordinal))
		{
			return;
		}

		HasUpdateNotification = settingsViewModel.HasUpdate;
	}

	/// <summary>
	/// Ensures the requested tool has a tab in the current window. If a tab already exists we focus it; otherwise
	/// we create a new host control and wire the main view model as DataContext.
	/// </summary>
	private void OpenOrFocusToolTab(string key, string name, Func<System.Windows.FrameworkElement> controlFactory, PackIconMaterialKind icon = PackIconMaterialKind.Tools)
		=> shellToolCoordinatorService.OpenOrFocusToolTab(
			Tabs,
			this,
			key,
			name,
			controlFactory,
			tab => SelectedTab = tab,
			icon);

	private bool CanMoveTabToOrbit(object parameter)
	{
		var target = parameter ?? SelectedTab;
		return shellPresentationPolicyService.CanMoveTabToOrbit(target, orbitLayoutState.Items.Cast<object?>());
	}

	private void MoveTabToOrbit(object parameter)
	{
		var target = parameter ?? SelectedTab;
		if (target == null)
		{
			return;
		}

		if (target is SessionModel session)
		{
			sessionUiCoordinator.MoveItemToOrbit(session, HandleTabRemoval, this, "move-session-to-orbit");
			SelectedSession = session;
		}
		else if (target is Models.ToolTabItem tool)
		{
			sessionUiCoordinator.MoveItemToOrbit(tool, HandleTabRemoval, this, "move-tool-to-orbit");
		}

		TryOpenToolByKey(ShellPresentationPolicyService.OrbitViewToolKey);
		var orbitTab = shellPresentationPolicyService.FindToolTab(Tabs.Cast<object?>(), ShellPresentationPolicyService.OrbitViewToolKey);
		if (orbitTab != null)
		{
			SelectedTab = orbitTab;
		}

		CommandManager.InvalidateRequerySuggested();
	}

	public void AdoptSessionsIntoOrbitWorkspace()
	{
		var sessionTabs = Tabs.OfType<SessionModel>().ToList();
		if (sessionTabs.Count == 0)
		{
			return;
		}

		var firstMoved = sessionUiCoordinator.AdoptSessionsIntoOrbit(
			sessionTabs,
			HandleTabRemoval,
			"adopt-session-to-orbit");

		if (firstMoved != null)
		{
			SelectedSession = firstMoved;
		}

		var orbitTab = shellPresentationPolicyService.FindToolTab(Tabs.Cast<object?>(), ShellPresentationPolicyService.OrbitViewToolKey);
		if (orbitTab != null)
		{
			SelectedTab = orbitTab;
		}

		CommandManager.InvalidateRequerySuggested();
	}

	#endregion

	private bool CanMoveSessionToIndividualTabs(object parameter)
	{
		var target = parameter ?? SelectedSession;
		return shellPresentationPolicyService.CanMoveSessionToIndividualTabs(target, Tabs.Cast<object?>());
	}

	private void MoveSessionToIndividualTabs(object parameter)
	{
		var target = parameter ?? SelectedSession;
		if (target is not SessionModel session)
		{
			return;
		}

		sessionUiCoordinator.MoveSessionToMainTabs(session, Tabs, "move-session-to-main-tabs");

		SelectedSession = session;
		SelectedTab = session;
		CommandManager.InvalidateRequerySuggested();
	}

	public double FloatingMenuTop
	{
		get => floatingMenuTop;
		set
		{
			if (Math.Abs(floatingMenuTop - value) < 0.1)
				return;
			floatingMenuTop = value;
			Settings.Default.FloatingMenuTop = value;
			OnPropertyChanged(nameof(FloatingMenuTop));
		}
	}

	public double FloatingMenuOpacity
	{
		get => floatingMenuOpacity;
		set
		{
			var normalized = Math.Clamp(value, 0.3, 1);
			if (Math.Abs(floatingMenuOpacity - normalized) < 0.01)
				return;
			floatingMenuOpacity = normalized;
			Settings.Default.FloatingMenuOpacity = normalized;
			OnPropertyChanged(nameof(FloatingMenuOpacity));
		}
	}

	public double FloatingMenuBackgroundOpacity
	{
		get => floatingMenuBackgroundOpacity;
		set
		{
			var normalized = Math.Clamp(value, 0.2, 1);
			if (Math.Abs(floatingMenuBackgroundOpacity - normalized) < 0.01)
				return;
			floatingMenuBackgroundOpacity = normalized;
			Settings.Default.FloatingMenuBackgroundOpacity = normalized;
			OnPropertyChanged(nameof(FloatingMenuBackgroundOpacity));
		}
	}

	public bool IsFloatingMenuVisible
	{
		get => isFloatingMenuVisible;
		private set
		{
			if (isFloatingMenuVisible == value)
				return;
			isFloatingMenuVisible = value;
			OnPropertyChanged(nameof(IsFloatingMenuVisible));
		}
	}

	public bool IsFloatingMenuWelcomeVisible
	{
		get => isFloatingMenuWelcomeVisible;
		private set
		{
			if (isFloatingMenuWelcomeVisible == value)
				return;
			isFloatingMenuWelcomeVisible = value;
			OnPropertyChanged(nameof(IsFloatingMenuWelcomeVisible));
		}
	}

	public bool IsFloatingMenuExpanded
	{
		get => isFloatingMenuExpanded;
		set
		{
			if (isFloatingMenuExpanded == value)
				return;
			isFloatingMenuExpanded = value;
			if (value)
			{
				IsFloatingMenuWelcomeVisible = false;
			}
			OnPropertyChanged(nameof(IsFloatingMenuExpanded));
		}
	}

	public bool IsFloatingMenuDockOverlayVisible
	{
		get => isFloatingMenuDockOverlayVisible;
		set
		{
			if (isFloatingMenuDockOverlayVisible == value)
				return;
		 isFloatingMenuDockOverlayVisible = value;
		OnPropertyChanged(nameof(IsFloatingMenuDockOverlayVisible));
		if (!value)
		{
			FloatingMenuDockCandidate = FloatingMenuDockRegion.None;
			IsFloatingMenuClipping = false;
		}
	}
}

	public FloatingMenuDockRegion FloatingMenuDockCandidate
	{
		get => floatingMenuDockCandidate;
		set
		{
			if (floatingMenuDockCandidate == value)
				return;
			floatingMenuDockCandidate = value;
			OnPropertyChanged(nameof(FloatingMenuDockCandidate));
		}
	}

	/// <summary>
	/// Manual selection for active side when auto is off.
	/// </summary>
	public FloatingMenuDirection FloatingMenuDirection
	{
		get => floatingMenuDirection;
		set
		{
			if (floatingMenuDirection == value)
				return;
			floatingMenuDirection = value;
			Settings.Default.FloatingMenuDirection = value.ToString();
			OnPropertyChanged(nameof(FloatingMenuDirection));
			OnPropertyChanged(nameof(FloatingMenuActiveSide));
			OnPropertyChanged(nameof(FloatingMenuExpansionDirection));
			OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
			OnPropertyChanged(nameof(FloatingMenuPopupHorizontalOffset));
			OnPropertyChanged(nameof(FloatingMenuPopupVerticalOffset));
		}
	}

	/// <summary>
	/// The currently active side for the menu: when auto is on, the nearest edge; otherwise the manual selection.
	/// </summary>
	public FloatingMenuDirection FloatingMenuActiveSide => FloatingMenuAutoDirection ? autoActiveSide : FloatingMenuDirection;

	public PlacementMode FloatingMenuPopupPlacement
		=> FloatingMenuAutoDirection
			? floatingMenuGeometryService.OppositeToPlacement(FloatingMenuActiveSide)
			: floatingMenuGeometryService.ToPlacement(FloatingMenuDirection);

	public FloatingMenuDirection FloatingMenuExpansionDirection
		=> FloatingMenuAutoDirection
			? floatingMenuGeometryService.OppositeOf(FloatingMenuActiveSide)
			: FloatingMenuDirection;

	/// <summary>
	/// Horizontal offset to center the popup menu on the button's "clock position"
	/// For Left/Right placements, this centers the menu vertically on the button
	/// </summary>
	public double FloatingMenuPopupHorizontalOffset
	{
		get
		{
			var placement = FloatingMenuPopupPlacement;
			// For Top/Bottom placements, offset horizontally to center on button
			if (placement == PlacementMode.Top || placement == PlacementMode.Bottom)
			{
				// Button is 52px wide, we want the popup centered horizontally
				// The popup expands from the edge, so we need to offset by half the button width
				// to align the popup's center with the button's center
				return 0; // Top/Bottom placements already center horizontally by default
			}
			return 0; // Left/Right don't need horizontal offset
		}
	}

	/// <summary>
	/// Vertical offset to center the popup menu on the button's "clock position"
	/// For Top/Bottom placements, this centers the menu horizontally on the button
	/// </summary>
	public double FloatingMenuPopupVerticalOffset
	{
		get
		{
			var placement = FloatingMenuPopupPlacement;
			// For Left/Right placements, offset vertically to center on button
			if (placement == PlacementMode.Left || placement == PlacementMode.Right)
			{
				// Button is 52px tall, we want the popup centered vertically
				// The popup expands from the edge, so we need to offset by half the button height
				// to align the popup's center with the button's center
				return 0; // Left/Right placements already center vertically by default
			}
			return 0; // Top/Bottom don't need vertical offset
		}
	}

	public double FloatingMenuInactivitySeconds
	{
		get => floatingMenuInactivitySeconds;
		set
		{
			var normalized = Math.Clamp(value, 0.5, 8);
			if (Math.Abs(floatingMenuInactivitySeconds - normalized) < 0.05)
				return;
			floatingMenuInactivitySeconds = normalized;
			Settings.Default.FloatingMenuInactivitySeconds = normalized;
			OnPropertyChanged(nameof(FloatingMenuInactivitySeconds));
		}
	}

	public bool FloatingMenuAutoDirection
	{
		get => floatingMenuAutoDirection;
		set
		{
			if (floatingMenuAutoDirection == value)
				return;
			floatingMenuAutoDirection = value;
			Settings.Default.FloatingMenuAutoDirection = value;
			OnPropertyChanged(nameof(FloatingMenuAutoDirection));

			// when switching modes, recompute and notify dependents
			ComputeAutoActiveSide();
			OnPropertyChanged(nameof(FloatingMenuActiveSide));
			OnPropertyChanged(nameof(FloatingMenuExpansionDirection));
			OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
			OnPropertyChanged(nameof(FloatingMenuPopupHorizontalOffset));
			OnPropertyChanged(nameof(FloatingMenuPopupVerticalOffset));
		}
	}

	public double FloatingMenuDockEdgeThreshold
	{
		get => Settings.Default.FloatingMenuDockEdgeThreshold;
		set
		{
			var normalized = Math.Clamp(value, 40, 200);
			if (Math.Abs(Settings.Default.FloatingMenuDockEdgeThreshold - normalized) < 0.1)
				return;
			Settings.Default.FloatingMenuDockEdgeThreshold = normalized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuDockEdgeThreshold));
		}
	}

	public double FloatingMenuDockCornerThreshold
	{
		get => Settings.Default.FloatingMenuDockCornerThreshold;
		set
		{
			var normalized = Math.Clamp(value, 60, 250);
			if (Math.Abs(Settings.Default.FloatingMenuDockCornerThreshold - normalized) < 0.1)
				return;
			Settings.Default.FloatingMenuDockCornerThreshold = normalized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuDockCornerThreshold));
			OnPropertyChanged(nameof(FloatingMenuDockCornerRadius));
		}
	}

	public double FloatingMenuDockCornerHeight
	{
		get => Settings.Default.FloatingMenuDockCornerHeight;
		set
		{
			var normalized = Math.Clamp(value, 60, 250);
			if (Math.Abs(Settings.Default.FloatingMenuDockCornerHeight - normalized) < 0.1)
				return;
			Settings.Default.FloatingMenuDockCornerHeight = normalized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuDockCornerHeight));
			OnPropertyChanged(nameof(FloatingMenuDockCornerRadius));
		}
	}

	public double FloatingMenuDockCornerRoundness
	{
		get => Settings.Default.FloatingMenuDockCornerRoundness;
		set
		{
			var normalized = Math.Clamp(value, 0d, 1d);
			if (Math.Abs(Settings.Default.FloatingMenuDockCornerRoundness - normalized) < 0.01)
				return;
			Settings.Default.FloatingMenuDockCornerRoundness = normalized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuDockCornerRoundness));
			OnPropertyChanged(nameof(FloatingMenuDockCornerRadius));
		}
	}

	public double FloatingMenuDockEdgeCoverage
	{
		get => Settings.Default.FloatingMenuDockEdgeCoverage;
		set
		{
			var normalized = Math.Clamp(value, 0.05d, 0.95d);
			if (Math.Abs(Settings.Default.FloatingMenuDockEdgeCoverage - normalized) < 0.005)
				return;
			Settings.Default.FloatingMenuDockEdgeCoverage = normalized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuDockEdgeCoverage));
		}
	}

	public double FloatingMenuDockZoneOpacity
	{
		get => Settings.Default.FloatingMenuDockZoneOpacity;
		set
		{
			var normalized = Math.Clamp(value, 0.05d, 0.9d);
			if (Math.Abs(Settings.Default.FloatingMenuDockZoneOpacity - normalized) < 0.005)
				return;
			Settings.Default.FloatingMenuDockZoneOpacity = normalized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuDockZoneOpacity));
		}
	}

	public FloatingMenuQuickToggleMode FloatingMenuQuickToggleMode
	{
		get => ParseFloatingMenuQuickToggleMode(Settings.Default.FloatingMenuQuickToggle);
		set
		{
			var serialized = value.ToString();
			if (string.Equals(Settings.Default.FloatingMenuQuickToggle, serialized, StringComparison.Ordinal))
				return;

			Settings.Default.FloatingMenuQuickToggle = serialized;
			Settings.Default.Save();
			OnPropertyChanged(nameof(FloatingMenuQuickToggleMode));
		}
	}

	public bool ShowAllSnapZonesOnClip
	{
		get => Settings.Default.FloatingMenuShowAllSnapZonesOnClip;
		set
		{
			if (Settings.Default.FloatingMenuShowAllSnapZonesOnClip == value)
				return;
			Settings.Default.FloatingMenuShowAllSnapZonesOnClip = value;
			Settings.Default.Save();
			OnPropertyChanged(nameof(ShowAllSnapZonesOnClip));
		}
	}

	public bool ShowAllSnapZonesOnDrag
	{
		get => Settings.Default.FloatingMenuShowAllSnapZonesOnDrag;
		set
		{
			if (Settings.Default.FloatingMenuShowAllSnapZonesOnDrag == value)
				return;
			Settings.Default.FloatingMenuShowAllSnapZonesOnDrag = value;
			Settings.Default.Save();
			OnPropertyChanged(nameof(ShowAllSnapZonesOnDrag));
		}
	}

	public bool IsFloatingMenuDragging
	{
		get => isFloatingMenuDragging;
		set
		{
			if (isFloatingMenuDragging == value)
				return;
			isFloatingMenuDragging = value;
			OnPropertyChanged(nameof(IsFloatingMenuDragging));
		}
	}

	public bool IsFloatingMenuClipping
	{
		get => isFloatingMenuClipping;
		private set
		{
			if (isFloatingMenuClipping == value)
				return;
			isFloatingMenuClipping = value;
			OnPropertyChanged(nameof(IsFloatingMenuClipping));
		}
	}

	public CornerRadius FloatingMenuDockCornerRadius
		=> floatingMenuGeometryService.BuildDockCornerRadius(
			Settings.Default.FloatingMenuDockCornerThreshold,
			Settings.Default.FloatingMenuDockCornerHeight,
			Settings.Default.FloatingMenuDockCornerRoundness);

	// Menu Button Visibility Properties
	public bool ShowMenuAddSession
	{
		get => Settings.Default.ShowMenuAddSession;
		set
		{
			if (Settings.Default.ShowMenuAddSession == value) return;
			Settings.Default.ShowMenuAddSession = value;
			OnPropertyChanged(nameof(ShowMenuAddSession));
		}
	}

	public bool ShowMenuInject
	{
		get => Settings.Default.ShowMenuInject;
		set
		{
			if (Settings.Default.ShowMenuInject == value) return;
			Settings.Default.ShowMenuInject = value;
			OnPropertyChanged(nameof(ShowMenuInject));
			OnPropertyChanged(nameof(ShowInjectMenuButton));
		}
	}

	public bool ShowInjectMenuButton => ShowMenuInject && !AutoInjectOnReady;

	public bool ShowMenuSessions
	{
		get => Settings.Default.ShowMenuSessions;
		set
		{
			if (Settings.Default.ShowMenuSessions == value) return;
			Settings.Default.ShowMenuSessions = value;
			OnPropertyChanged(nameof(ShowMenuSessions));
		}
	}

	public bool ShowMenuAccountManager
	{
		get => Settings.Default.ShowMenuAccountManager;
		set
		{
			if (Settings.Default.ShowMenuAccountManager == value) return;
			Settings.Default.ShowMenuAccountManager = value;
			OnPropertyChanged(nameof(ShowMenuAccountManager));
		}
	}

	public bool ShowMenuScriptControls
	{
		get => Settings.Default.ShowMenuScriptControls;
		set
		{
			if (Settings.Default.ShowMenuScriptControls == value) return;
			Settings.Default.ShowMenuScriptControls = value;
			OnPropertyChanged(nameof(ShowMenuScriptControls));
		}
	}

	public bool ShowMenuThemeManager
	{
		get => Settings.Default.ShowMenuThemeManager;
		set
		{
			if (Settings.Default.ShowMenuThemeManager == value) return;
			Settings.Default.ShowMenuThemeManager = value;
			OnPropertyChanged(nameof(ShowMenuThemeManager));
		}
	}

	public bool ShowMenuConsole
	{
		get => Settings.Default.ShowMenuConsole;
		set
		{
			if (Settings.Default.ShowMenuConsole == value) return;
			Settings.Default.ShowMenuConsole = value;
			OnPropertyChanged(nameof(ShowMenuConsole));
		}
	}

	public bool ShowMenuMcpControl
	{
		get => Settings.Default.ShowMenuMcpControl;
		set
		{
			if (Settings.Default.ShowMenuMcpControl == value) return;
			Settings.Default.ShowMenuMcpControl = value;
			OnPropertyChanged(nameof(ShowMenuMcpControl));
		}
	}

	public bool ShowMenuGuide
	{
		get => Settings.Default.ShowMenuApiDocumentation;
		set
		{
			if (Settings.Default.ShowMenuApiDocumentation == value) return;
			Settings.Default.ShowMenuApiDocumentation = value;
			OnPropertyChanged(nameof(ShowMenuGuide));
		}
	}

	public bool ShowMenuSettings
	{
		get => Settings.Default.ShowMenuSettings;
		set
		{
			if (Settings.Default.ShowMenuSettings == value) return;
			Settings.Default.ShowMenuSettings = value;
			OnPropertyChanged(nameof(ShowMenuSettings));
		}
	}

	public bool ShowMenuToolsOverview
	{
		get => Settings.Default.ShowMenuToolsOverview;
		set
		{
			if (Settings.Default.ShowMenuToolsOverview == value) return;
			Settings.Default.ShowMenuToolsOverview = value;
			OnPropertyChanged(nameof(ShowMenuToolsOverview));
		}
	}

	public bool ShowMenuSessionGallery
	{
		get => Settings.Default.ShowMenuSessionGallery;
		set
		{
			if (Settings.Default.ShowMenuSessionGallery == value) return;
			Settings.Default.ShowMenuSessionGallery = value;
			OnPropertyChanged(nameof(ShowMenuSessionGallery));
		}
	}

	public bool ShowMenuOrbitView
	{
		get => Settings.Default.ShowMenuOrbitView;
		set
		{
			if (Settings.Default.ShowMenuOrbitView == value) return;
			Settings.Default.ShowMenuOrbitView = value;
			OnPropertyChanged(nameof(ShowMenuOrbitView));
		}
	}

	public bool ShowMenuSharpBuilder
	{
		get => Settings.Default.ShowMenuSharpBuilder;
		set
		{
			if (Settings.Default.ShowMenuSharpBuilder == value) return;
			Settings.Default.ShowMenuSharpBuilder = value;
			OnPropertyChanged(nameof(ShowMenuSharpBuilder));
		}
	}

	public bool ShowFloatingMenuOnHome
	{
		get => Settings.Default.ShowFloatingMenuOnHome;
		set
		{
			if (Settings.Default.ShowFloatingMenuOnHome == value) return;
			Settings.Default.ShowFloatingMenuOnHome = value;
			OnPropertyChanged(nameof(ShowFloatingMenuOnHome));
			UpdateFloatingMenuVisibilityForCurrentTab();
		}
	}

	public bool ShowFloatingMenuOnSessionTabs
	{
		get => Settings.Default.ShowFloatingMenuOnSessionTabs;
		set
		{
			if (Settings.Default.ShowFloatingMenuOnSessionTabs == value) return;
			Settings.Default.ShowFloatingMenuOnSessionTabs = value;
			OnPropertyChanged(nameof(ShowFloatingMenuOnSessionTabs));
			UpdateFloatingMenuVisibilityForCurrentTab();
		}
	}

	public bool ShowFloatingMenuOnToolTabs
	{
		get => Settings.Default.ShowFloatingMenuOnToolTabs;
		set
		{
			if (Settings.Default.ShowFloatingMenuOnToolTabs == value) return;
			Settings.Default.ShowFloatingMenuOnToolTabs = value;
			OnPropertyChanged(nameof(ShowFloatingMenuOnToolTabs));
			UpdateFloatingMenuVisibilityForCurrentTab();
		}
	}

	public void UpdateFloatingMenuWelcomeHint(bool enabled)
	{
		if (enabled && !IsFloatingMenuExpanded)
		{
			IsFloatingMenuWelcomeVisible = true;
		}
		else if (!enabled)
		{
			IsFloatingMenuWelcomeVisible = false;
		}
	}

	private void UpdateFloatingMenuVisibilityForCurrentTab()
	{
		if (!ShouldShowFloatingMenuForCurrentTab())
		{
			HideFloatingMenu();
			return;
		}

		IsFloatingMenuVisible = true;
	}

	private void OnFloatingMenuWelcomeInvoked()
	{
		IsFloatingMenuWelcomeVisible = false;
		if (!IsFloatingMenuExpanded)
		{
			IsFloatingMenuExpanded = true;
		}
	}

	public void SetFloatingMenuClipping(bool clipped)
	{
		IsFloatingMenuClipping = clipped;
	}

	public void UpdateFloatingMenuPosition(double left, double top, double hostWidth, double hostHeight)
	{
		FloatingMenuLeft = left;
		FloatingMenuTop = top;
		UpdateHostViewport(hostWidth, hostHeight);
	}

	public void ApplyDockRegion(FloatingMenuDockRegion region, double handleWidth, double handleHeight)
	{
		if (region == FloatingMenuDockRegion.None)
		{
			return;
		}

		var position = floatingMenuGeometryService.ComputeDockPosition(
			region,
			FloatingMenuLeft,
			FloatingMenuTop,
			handleWidth,
			handleHeight,
			hostViewportWidth,
			hostViewportHeight);

		FloatingMenuLeft = position.Left;
		FloatingMenuTop = position.Top;

		ComputeAutoActiveSide();

		if (!FloatingMenuAutoDirection)
		{
			FloatingMenuDirection = floatingMenuGeometryService.DetermineDirectionForRegion(region);
		}
	}

	public void UpdateHostViewport(double width, double height)
	{
		if (width <= 0 || height <= 0)
			return;

		hostViewportWidth = width;
		hostViewportHeight = height;

		ComputeAutoActiveSide();
	}

	public bool ShowFloatingMenu(bool force = false)
	{
		if (!force && !ShouldShowFloatingMenuForCurrentTab())
		{
			IsFloatingMenuVisible = false;
			return false;
		}

		IsFloatingMenuVisible = true;
		return true;
	}

	public void HideFloatingMenu()
	{
		IsFloatingMenuExpanded = false;
		IsFloatingMenuVisible = false;
	}

	public bool ShouldShowFloatingMenuForCurrentTab()
	{
		return floatingMenuVisibilityService.ShouldShowForCurrentTab(
			SelectedTab,
			Tabs.Count,
			ShowFloatingMenuOnHome,
			ShowFloatingMenuOnSessionTabs,
			ShowFloatingMenuOnToolTabs);
	}

	private static FloatingMenuQuickToggleMode ParseFloatingMenuQuickToggleMode(string value)
	{
		if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, out FloatingMenuQuickToggleMode mode))
		{
			return mode;
		}

		return FloatingMenuQuickToggleMode.MiddleMouse;
	}

	private void ComputeAutoActiveSide()
	{
		autoActiveSide = floatingMenuGeometryService.ComputeAutoActiveSide(
			FloatingMenuLeft,
			FloatingMenuTop,
			hostViewportWidth,
			hostViewportHeight,
			FloatingMenuDirection);
		OnPropertyChanged(nameof(FloatingMenuActiveSide));
		OnPropertyChanged(nameof(FloatingMenuExpansionDirection));
		OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
		OnPropertyChanged(nameof(FloatingMenuPopupHorizontalOffset));
		OnPropertyChanged(nameof(FloatingMenuPopupVerticalOffset));
	}

	private async Task LoadScriptAsync()
	{
		if (!CanLoadScript())
		{
			if (!Settings.Default.MesharpIntegrationEnabled)
			{
				ConsoleLog.Append(
					"[OrbitCmd] MESharp integration is disabled in Settings -> Advanced. Script load is unavailable.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
			}
			return;
		}

		var targetSession = ResolveHotReloadTarget();
		if (targetSession == null)
		{
			ConsoleLog.Append("[OrbitCmd] No active session available for load.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return;
		}

		await scriptOrchestrationService.LoadAsync(targetSession, hotReloadScriptPath).ConfigureAwait(false);
	}

		private void ApplyThemeFromSettings()
			=> themeService.ApplySavedTheme();

		private void ToggleConsole()
			=> OpenConsoleTab();

		private void BrowseForScript()
		{
			var dialog = new OpenFileDialog
			{
				Title = "Select managed script",
				Filter = "Managed assemblies (*.dll)|*.dll|All files (*.*)|*.*",
				CheckFileExists = true,
				FileName = Path.GetFileName(hotReloadScriptPath)
			};

			if (!string.IsNullOrWhiteSpace(hotReloadScriptPath))
			{
				var dir = Path.GetDirectoryName(hotReloadScriptPath);
				if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
				{
					dialog.InitialDirectory = dir;
				}
			}

			if (dialog.ShowDialog() == true)
			{
				HotReloadScriptPath = dialog.FileName;
			}
		}

	private async Task ReloadScriptAsync()
	{
		if (!CanReloadScript())
		{
			if (!Settings.Default.MesharpIntegrationEnabled)
			{
				ConsoleLog.Append(
					"[OrbitCmd] MESharp integration is disabled in Settings -> Advanced. Script reload is unavailable.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
			}
			return;
		}

		var targetSession = ResolveHotReloadTarget();
		if (targetSession == null)
		{
			ConsoleLog.Append("[OrbitCmd] No active session available for reload.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return;
		}

		await scriptOrchestrationService.ReloadAsync(targetSession, hotReloadScriptPath).ConfigureAwait(false);
	}

		private void OnSessionPropertyChanged(object sender, PropertyChangedEventArgs e)
		{
			if (sender is not SessionModel session)
			{
				return;
			}

			if (e.PropertyName == nameof(SessionModel.InjectionState))
			{
				CommandManager.InvalidateRequerySuggested();
			}
		}

			private void OnSelectedSessionChanged(object sender, PropertyChangedEventArgs e)
			{
				if (e.PropertyName == nameof(SessionModel.State) ||
					e.PropertyName == nameof(SessionModel.InjectionState))
				{
				CommandManager.InvalidateRequerySuggested();
			}
		}

	private void OnGlobalSessionChanged(object sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SessionCollectionService.GlobalHotReloadTargetSession))
		{
			var sharedTarget = sessionCollectionService.GlobalHotReloadTargetSession;
			if (!ReferenceEquals(hotReloadTargetSession, sharedTarget))
			{
				hotReloadTargetSession = sharedTarget;
				OnPropertyChanged(nameof(HotReloadTargetSession));
				CommandManager.InvalidateRequerySuggested();
			}

			return;
		}

		if (e.PropertyName == nameof(SessionCollectionService.GlobalSelectedSession))
		{
			// If this window doesn't have a local selected session, use the global one
			if (SelectedSession == null)
			{
					CommandManager.InvalidateRequerySuggested();
				}
			}
		}

	public void ActivateSession(SessionModel session)
	{
		if (session == null)
			return;

		var ownerVm = ResolveOwningShellForSession(session);
		shellSessionFocusService.RevealSession(ownerVm, session, requestFocus: false);
		ownerVm.TryActivateOwningWindow();
	}

	private MainWindowViewModel ResolveOwningShellForSession(SessionModel session)
		=> shellSessionFocusService.ResolveOwningShellForSession(this, session);

	private void RevealSession(SessionModel session, bool requestFocus)
		=> shellSessionFocusService.RevealSession(this, session, requestFocus);

	private void TryActivateOwningWindow()
		=> shellSessionFocusService.ActivateOwningWindow(this);

	/// <summary>
	/// Reasserts MESharp input passthrough and focus spoof state for injected sessions.
	/// This mirrors the WPF debug app behavior so Orbit can reliably accept keyboard input.
	/// </summary>
	public Task ReassertInputPassthroughAsync(bool orbitActive)
		=> mesharpSessionCommandService.ReassertInputPassthroughAsync(Sessions.Cast<SessionModel?>());

	public bool IsMesharpDebugMenuHotkeyEnabled
		=> Settings.Default.MesharpIntegrationEnabled && Settings.Default.MesharpDebugMenuHotkeyEnabled;

	public bool MatchesMesharpDebugMenuHotkey(Key key, ModifierKeys modifiers)
	{
		return mesharpHotkeyService.MatchesDebugMenuHotkey(
			Settings.Default.MesharpIntegrationEnabled,
			Settings.Default.MesharpDebugMenuHotkeyEnabled,
			Settings.Default.MesharpDebugMenuHotkey,
			key,
			modifiers);
	}

	public Task ToggleNativeDebugMenuAsync()
		=> ToggleNativeDebugMenuAsync(SelectedSession);

	public Task ToggleNativeDebugMenuAsync(SessionModel? session)
	{
		if (!CanToggleNativeDebugMenu(session))
		{
			return Task.CompletedTask;
		}

		return SetNativeDebugMenuVisibleAsync(session!, !session!.NativeDebugMenuVisible);
	}

	public bool CanToggleNativeDebugMenu(SessionModel? session)
		=> mesharpSessionCommandService.CanToggleNativeDebugMenu(Settings.Default.MesharpIntegrationEnabled, session);

	public Task SetNativeDebugMenuVisibleAsync(bool visible)
		=> mesharpSessionCommandService.SetNativeDebugMenuVisibleAsync(Sessions.Cast<SessionModel?>(), visible);

	public Task SetNativeDebugMenuVisibleAsync(SessionModel session, bool visible)
		=> mesharpSessionCommandService.SetNativeDebugMenuVisibleAsync(
			session,
			visible,
			revealAndFocusAsync: target =>
			{
				var ownerVm = ResolveOwningShellForSession(target);
				ownerVm.RevealSession(target, requestFocus: true);
				ownerVm.TryActivateOwningWindow();
				return Task.CompletedTask;
			});

	public Task ApplyNativeDebugMenuInjectionPreferenceAsync(bool hideOnInject)
		=> mesharpSessionCommandService.ApplyNativeDebugMenuInjectionPreferenceAsync(Sessions.Cast<SessionModel?>(), hideOnInject);

	public void FocusSession(SessionModel session)
	{
		if (session == null)
		{
			return;
		}

		var ownerVm = ResolveOwningShellForSession(session);
		ownerVm.RevealSession(session, requestFocus: true);
		ownerVm.TryActivateOwningWindow();
	}

	public void CloseSession(SessionModel session)
	{
		if (session == null)
			return;

		_ = CloseSessionInternalAsync(session, skipConfirmation: false, forceKillOnTimeout: false);
	}

	public void CloseSessionFromTab(SessionModel session)
	{
		if (session == null)
		{
			return;
		}

		_ = CloseSessionInternalAsync(session, skipConfirmation: false, forceKillOnTimeout: false);
	}

		public async Task CloseAllSessionsAsync(bool skipConfirmation, bool forceKillOnTimeout = false)
		{
			var sessionsSnapshot = Sessions.OfType<SessionModel>().ToList();
			var closeTasks = sessionsSnapshot
				.Select(session => CloseSessionInternalAsync(session, skipConfirmation, forceKillOnTimeout))
				.ToList();
			await Task.WhenAll(closeTasks).ConfigureAwait(true);
		}

		public Task ShutdownTrackedProcessesAsync(bool forceKillOnTimeout = false, CancellationToken cancellationToken = default)
			=> shellSessionCloseService.ShutdownTrackedProcessesAsync(forceKillOnTimeout, cancellationToken);

		private async Task CloseSessionInternalAsync(SessionModel session, bool skipConfirmation, bool forceKillOnTimeout = false)
		{
			await shellSessionCloseService.CloseSessionAsync(
				session,
				skipConfirmation,
				forceKillOnTimeout,
				Sessions,
				() => orbitLayoutState.Items.Cast<object>(),
				ConfirmSessionCloseAsync,
				RemoveSessionFromTabShell,
				EnsureSessionRemainsVisible).ConfigureAwait(true);
		}

		private bool RemoveSessionFromTabShell(SessionModel session)
		{
			var removedAny = false;

			if (sessionUiCoordinator.RemoveSessionFromVisibleShell(session, HandleTabRemoval, "remove-session-from-visible-shell"))
			{
				removedAny = true;
			}

			return removedAny;
		}

		private void EnsureSessionRemainsVisible(SessionModel session)
		{
			shellSessionRecoveryService.EnsureSessionRemainsVisible(
				session,
				Sessions,
				Tabs,
				orbitLayoutState.Items.Cast<object>(),
				selected => SelectedSession = selected,
				selected => SelectedTab = selected);
		}

	private async Task<bool> ConfirmSessionCloseAsync(SessionModel session)
	{
		if (!sessionLifecycleCoordinator.ShouldConfirmClose(session))
		{
			return true;
		}

		return await ShowSessionCloseDialogAsync(session).ConfigureAwait(true);
	}

		private async Task<bool> ShowSessionCloseDialogAsync(SessionModel session)
	{
		bool ShowDialogOnUi()
		{
			var viewModel = new SessionCloseDialogViewModel(session);
			var dialog = new Views.SessionCloseDialog(viewModel)
			{
				Owner = Application.Current?.MainWindow
			};

				dialog.ShowDialog();
				return dialog.DialogResult == true;
			}

		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null)
		{
			return ShowDialogOnUi();
		}

		if (dispatcher.CheckAccess())
		{
			return ShowDialogOnUi();
		}

			return await dispatcher.InvokeAsync(ShowDialogOnUi).Task.ConfigureAwait(true);
		}

		private void OnTabsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
		{
		if (e == null)
		{
			UpdateFloatingMenuVisibilityForCurrentTab();
			return;
		}

		var result = shellTabCollectionCoordinatorService.HandleCollectionChanged(
			Tabs.Cast<object?>(),
			SelectedTab,
			e.Action,
			e.OldItems?.Cast<object?>(),
			e.NewItems?.Cast<object?>(),
			IsOrbitViewTearOffWindow(),
			ReferenceEquals(Application.Current?.MainWindow?.DataContext, this));

		if (!ReferenceEquals(SelectedTab, result.SelectedTab))
		{
			SelectedTab = result.SelectedTab;
		}

		if (result.ClearSelectedSession)
		{
			SelectedSession = null;
		}

		foreach (var removedSession in result.SessionsNeedingOrphanValidation)
		{
			ScheduleOrphanedSessionValidation(removedSession);
		}

			UpdateFloatingMenuVisibilityForCurrentTab();
		}

		private bool IsOrbitViewTearOffWindow()
		{
			var ownership = sessionReconciliationService.CaptureUiOwnership(orbitLayoutState.Items.Cast<object>());
			return ownership.OrbitWindows.Any(window => ReferenceEquals(window.DataContext, this));
		}

	private static object? ResolveClosingItem(ItemActionCallbackArgs<TabablzControl> args)
	{
		if (args?.DragablzItem == null)
		{
			return null;
		}

		return args.DragablzItem.DataContext ?? args.DragablzItem.Content;
	}

	private void ScheduleOrphanedSessionValidation(SessionModel session)
	{
		if (session == null)
		{
			return;
		}

		if (!sessionLifecycleCoordinator.TryBeginOrphanValidation(session))
		{
			return;
		}

		var dispatcher = Application.Current?.Dispatcher;
		if (dispatcher == null)
		{
			ValidateOrphanedSession(session);
			return;
		}

		dispatcher.BeginInvoke(
			new Action(() => ValidateOrphanedSession(session)),
			System.Windows.Threading.DispatcherPriority.Background);
	}

	private void ValidateOrphanedSession(SessionModel session)
	{
		shellSessionRecoveryService.ValidateOrphanedSession(
			session,
			_disposed,
			orbitLayoutState.Items.Cast<object>(),
			EnsureSessionRemainsVisible);
	}

		private async void SessionCrashWatchTimer_Tick(object? sender, EventArgs e)
		{
			if (_disposed || isApplicationShuttingDown)
			{
				return;
			}

			if (!ReferenceEquals(Application.Current?.MainWindow?.DataContext, this))
			{
				return;
			}

			await sessionAutoRelaunchService.CheckAndRelaunchAsync(
				Sessions.Cast<SessionModel?>(),
				isApplicationShuttingDown,
				Settings.Default.AutoRelaunchOnUnexpectedExit,
				session => CloseSessionInternalAsync(session, skipConfirmation: true, forceKillOnTimeout: false),
				AddSingleSessionAsync).ConfigureAwait(true);
		}

		public void MarkApplicationShuttingDown()
		{
			isApplicationShuttingDown = true;
			sessionCrashWatchTimer?.Stop();
		}

		private void HandleTabRemoval(object removedItem)
		{
			var selection = shellTabSelectionService.ResolveAfterTabRemoval(Tabs.Cast<object?>(), SelectedTab, removedItem);
			if (!ReferenceEquals(SelectedTab, selection.SelectedTab))
			{
				SelectedTab = selection.SelectedTab;
			}

			if (selection.ClearSelectedSession)
			{
				SelectedSession = null;
			}

				UpdateFloatingMenuVisibilityForCurrentTab();
			}

		private void DisposeToolItem(object? item)
			=> shellToolCoordinatorService.DisposeToolItem(item, this);

		private void RestoreOrbitWorkspaceToTabs()
		{
			sessionUiCoordinator.RestoreOrbitWorkspaceToTabs(Tabs, this, "restore-orbit-workspace");
		}

		private void OnSessionsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
			if (e.NewItems != null)
			{
				foreach (var item in e.NewItems.OfType<SessionModel>())
				{
					item.PropertyChanged += OnSessionPropertyChanged;
				}
			}

			if (e.OldItems != null)
			{
				foreach (var item in e.OldItems.OfType<SessionModel>())
				{
					item.PropertyChanged -= OnSessionPropertyChanged;
					sessionPlacementService.Remove(item);
				}
			}

		var selection = shellSessionSelectionService.ResolveAfterSessionsChanged(
			Sessions,
			e.Action,
			SelectedSession,
			hotReloadTargetSession);
		if (!ReferenceEquals(hotReloadTargetSession, selection.HotReloadTargetSession))
		{
			HotReloadTargetSession = selection.HotReloadTargetSession;
		}

		if (!ReferenceEquals(SelectedSession, selection.SelectedSession))
		{
			SelectedSession = selection.SelectedSession;
		}

		OnPropertyChanged(nameof(HasSessions));
		ShowSessionsCommand?.NotifyCanExecuteChanged();
		InjectCommand?.NotifyCanExecuteChanged();
		LoadScriptCommand?.NotifyCanExecuteChanged();
		ReloadScriptCommand?.NotifyCanExecuteChanged();
		CommandManager.InvalidateRequerySuggested();
	}

		#region IDisposable Implementation

		private bool _disposed = false;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			// Dispose active tool view models owned by this window.
			foreach (var tool in Tabs.OfType<Models.ToolTabItem>().ToList())
			{
				DisposeToolItem(tool);
			}

			// Unsubscribe from collection events
			try
			{
				Tabs.CollectionChanged -= OnTabsCollectionChanged;
				Sessions.CollectionChanged -= OnSessionsCollectionChanged;
					sessionCollectionService.PropertyChanged -= OnGlobalSessionChanged;
					settingsViewModel.PropertyChanged -= OnSettingsViewModelPropertyChanged;
					sessionLifecycleCoordinator.ClearPendingOrphanValidations();
				sessionCrashWatchTimer.Stop();
				sessionCrashWatchTimer.Tick -= SessionCrashWatchTimer_Tick;
			}
			catch
			{
				// Ignore errors during cleanup
			}

			// Unsubscribe from all session events
			foreach (var session in Sessions)
			{
				try
				{
					session.PropertyChanged -= OnSessionPropertyChanged;
				}
				catch
				{
					// Ignore errors during cleanup
				}
			}

			// Unsubscribe from selected session
			if (selectedSession != null)
			{
				try
				{
					selectedSession.PropertyChanged -= OnSelectedSessionChanged;
				}
				catch
				{
					// Ignore errors during cleanup
				}
			}
		}

		#endregion

		protected virtual void OnPropertyChanged(string propertyName)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		//private void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = "")
		//{
		//	// Forward to the existing protected overload which raises PropertyChanged.
		//	// Using CallerMemberName lets callers call OnPropertyChanged() with no args.
		//	OnPropertyChanged(propertyName ?? string.Empty);
		//}
	}
}
