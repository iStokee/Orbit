using Dragablz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Orbit.Tooling;
using Orbit.Views;
using Orbit;
using System;
using System.Collections.Generic;
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
		// Keys used to retrieve shared tool metadata from the registry. Keeping them here makes it easy to see
		// which panels participate in the dynamic tool surface.
	private const string AccountManagerToolKey = "AccountManager";
	private const string SettingsToolKey = "Settings";
	private const string ConsoleToolKey = "Console";
	private const string ThemeManagerToolKey = "ThemeManager";
	private const string SessionsOverviewToolKey = "SessionsOverview";
	private const string ScriptManagerToolKey = "ScriptManager";
	private const string GuideToolKey = "ApiDocumentation";
	private const string ToolsOverviewToolKey = "UnifiedToolsManager";
	private const string FsmNodeEditorToolKey = "FsmNodeEditor";

	private readonly SessionManagerService sessionManager;
	private readonly ThemeService themeService;
	private readonly ScriptIntegrationService scriptIntegrationService;
	private readonly ScriptManagerService scriptManagerService;
	private readonly AccountService accountService;
	private readonly AutoLoginService autoLoginService;
	private readonly SessionCollectionService sessionCollectionService;
	private readonly OrbitLayoutStateService orbitLayoutState;
	private readonly ConsoleLogService consoleLogService;
	private readonly IToolRegistry toolRegistry;
	private readonly InterTabClient interTabClient;
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

	/// <summary>
	/// Creates the main window view model. Most dependencies are long-lived services shared via DI;
	/// the constructor wires them so detached windows can reuse the same singletons.
	/// </summary>
	public MainWindowViewModel(
		SessionManagerService sessionManager,
		ThemeService themeService,
		ScriptIntegrationService scriptIntegrationService,
		ScriptManagerService scriptManagerService,
		AccountService accountService,
		AutoLoginService autoLoginService,
		SessionCollectionService sessionCollectionService,
		OrbitLayoutStateService orbitLayoutStateService,
		ConsoleLogService consoleLogService,
		InterTabClient interTabClient,
		IToolRegistry toolRegistry)
	{
		this.sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
		this.themeService = themeService ?? throw new ArgumentNullException(nameof(themeService));
		this.scriptIntegrationService = scriptIntegrationService ?? throw new ArgumentNullException(nameof(scriptIntegrationService));
		this.scriptManagerService = scriptManagerService ?? throw new ArgumentNullException(nameof(scriptManagerService));
		this.accountService = accountService ?? throw new ArgumentNullException(nameof(accountService));
		this.autoLoginService = autoLoginService ?? throw new ArgumentNullException(nameof(autoLoginService));
		this.sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
		this.orbitLayoutState = orbitLayoutStateService ?? throw new ArgumentNullException(nameof(orbitLayoutStateService));
		this.consoleLogService = consoleLogService ?? throw new ArgumentNullException(nameof(consoleLogService));
		this.interTabClient = interTabClient ?? throw new ArgumentNullException(nameof(interTabClient));
		this.toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));

		ScriptManager = this.scriptManagerService;
		AccountService = this.accountService;
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
		OpenSessionGalleryCommand = new RelayCommand(() => TryOpenToolByKey("SessionGallery"));
	OpenOrbitViewCommand = new RelayCommand(() => TryOpenToolByKey("OrbitView"));
	MoveTabToOrbitCommand = new RelayCommand<object?>(MoveTabToOrbit, CanMoveTabToOrbit);
	MoveSessionToIndividualTabsCommand = new RelayCommand<object?>(MoveSessionToIndividualTabs, CanMoveSessionToIndividualTabs);
	OpenThemeManagerCommand = new RelayCommand(OpenThemeManager);
		OpenScriptManagerCommand = new RelayCommand(OpenScriptManager);
		OpenFsmNodeEditorCommand = new RelayCommand(OpenFsmNodeEditor);
		OpenAccountManagerCommand = new RelayCommand(OpenAccountManager, () => this.toolRegistry.Find(AccountManagerToolKey) != null);
		OpenGuideCommand = new RelayCommand(OpenGuideTab);
		OpenSettingsCommand = new RelayCommand(OpenSettingsTab);
		OpenToolsOverviewCommand = new RelayCommand(OpenToolsOverviewTab);
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
	public IRelayCommand OpenFsmNodeEditorCommand { get; }
	public IRelayCommand ToggleConsoleCommand { get; }
	public IRelayCommand BrowseScriptCommand { get; }
	public IRelayCommand LoadScriptCommand { get; }
	public IRelayCommand ReloadScriptCommand { get; }
	public IRelayCommand<object?> BeginSessionRenameCommand { get; }
	public IRelayCommand<object?> CommitSessionRenameCommand { get; }
	public IRelayCommand<object?> CancelSessionRenameCommand { get; }
	public IRelayCommand FloatingMenuWelcomeCommand { get; }
	public ConsoleLogService ConsoleLog { get; }
	public AccountService AccountService { get; }
	public IRelayCommand OpenAccountManagerCommand { get; }
	public IRelayCommand OpenGuideCommand { get; }
	public IRelayCommand OpenSettingsCommand { get; }
	public IRelayCommand OpenToolsOverviewCommand { get; }
	public Array FloatingMenuDirectionOptions { get; }
	public Array FloatingMenuQuickToggleModes { get; }

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

			if (value != null && (hotReloadTargetSession == null || !Sessions.Contains(hotReloadTargetSession)))
			{
				HotReloadTargetSession = value;
			}
			else if (hotReloadTargetSession != null && !Sessions.Contains(hotReloadTargetSession))
			{
				HotReloadTargetSession = Sessions.FirstOrDefault();
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
			var hostControl = new ChildClientView();

			var session = new SessionModel
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now,
				HostControl = hostControl,
				RequireInjectionBeforeDock = AutoInjectOnReady
			};

			hostControl.DataContext = session;

			// Always add to Sessions collection
			Sessions.Add(session);

			// Check launch behavior setting
			var launchBehavior = Settings.Default.SessionLaunchBehavior;

			if (launchBehavior == "OrbitView" || launchBehavior == "SessionsTabbed")
			{
				// Don't add to Tabs - add to Orbit View workspace explicitly.
				orbitLayoutState.AddItem(session);
				SelectedSession = session;
				// If Orbit View isn't open, open it
				if (!Tabs.Any(t => t is FrameworkElement fe && fe.GetType().Name.Contains("OrbitGridLayoutView")))
				{
					OpenOrbitViewCommand?.Execute(null);
				}
			}
			else if (launchBehavior == "Ask")
			{
				// TODO: Show dialog asking user where to dock
				// For now, default to individual tabs
				Tabs.Add(session);
				SelectedSession = session;
				SelectedTab = session;
			}
			else // IndividualTabs or default
			{
				Tabs.Add(session);
				SelectedSession = session;
				SelectedTab = session; // Auto-focus the new tab
			}

		try
		{
			await sessionManager.InitializeSessionAsync(session);

			// Auto-inject as soon as the session is ready (if enabled)
			if (AutoInjectOnReady && session.InjectionState == InjectionState.Ready)
			{
				Console.WriteLine($"[Orbit] Session '{session.Name}' is ready, auto-injecting...");
				await InjectSessionInternalAsync(session);
			}
		}
		catch (Exception ex)
		{
			Console.WriteLine($"[Orbit] Session initialization/injection failed: {ex.Message}");
			// SessionManager already marks the failure on the model.
		}
			finally
			{
				CommandManager.InvalidateRequerySuggested();
			}
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
		if (TryOpenToolByKey(ScriptManagerToolKey))
			return;

		ConsoleLog.Append(
			"[Orbit] Script Manager tool is unavailable; falling back to local instance.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Warning);

		OpenOrFocusToolTab(
			key: ScriptManagerToolKey,
			name: "Script Manager",
			controlFactory: () => ResolveRequiredService<Views.ScriptManagerPanel>(),
			icon: PackIconMaterialKind.CodeBraces);
	}

	private void OpenFsmNodeEditor()
	{
		if (TryOpenToolByKey(FsmNodeEditorToolKey))
		{
			return;
		}

		ConsoleLog.Append(
			"[Orbit] Orbit Builder plugin is not loaded. Load it from Tools > Unified Tools Manager > Auto-load or Import Plugin.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Warning);
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
			if (args.DragablzItem.DataContext is SessionModel session)
			{
				args.Cancel();
				_ = CloseSessionInternalAsync(session, skipConfirmation: false, forceKillOnTimeout: true);
				return;
			}

			var removedItem = args.DragablzItem.DataContext;
			if (removedItem is Models.ToolTabItem toolItem &&
				string.Equals(toolItem.Key, "OrbitView", StringComparison.Ordinal))
			{
				RestoreOrbitWorkspaceToTabs();
			}
			DisposeToolItem(removedItem);
			Tabs.Remove(removedItem);
			HandleTabRemoval(removedItem);
		}

		private bool CanInject() => SelectedSession?.IsInjectable == true;

	private SessionModel? ResolveHotReloadTarget()
	{
		var sharedTarget = sessionCollectionService.GlobalHotReloadTargetSession;
		if (sharedTarget != null && Sessions.Contains(sharedTarget))
		{
			return sharedTarget;
		}

		if (hotReloadTargetSession != null && Sessions.Contains(hotReloadTargetSession))
		{
			return hotReloadTargetSession;
		}

		var fallback = SelectedSession ?? sessionCollectionService.GlobalSelectedSession;
		if (fallback != null && Sessions.Contains(fallback))
		{
			return fallback;
		}

		return Sessions.FirstOrDefault();
	}

	private bool CanLoadScript()
	{
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
		if (session == null)
			return;

		session.EditableName = session.Name ?? string.Empty;
		session.IsRenaming = true;
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
		if (session == null)
			return;

		var proposed = session.EditableName?.Trim();
		session.IsRenaming = false;

		if (string.IsNullOrEmpty(proposed))
		{
			session.EditableName = session.Name ?? string.Empty;
			return;
		}

		if (!string.Equals(session.Name, proposed, StringComparison.Ordinal))
		{
			session.Name = proposed;
		}

		session.EditableName = session.Name ?? string.Empty;
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
		if (session == null)
			return;

		session.IsRenaming = false;
		session.EditableName = session.Name ?? string.Empty;
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
		if (!TryOpenToolByKey(AccountManagerToolKey))
		{
			ConsoleLog.Append("[Orbit] Account Manager tool is unavailable.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
		}
	}

    public void OpenSettingsTab()
    {
        if (!TryOpenToolByKey(SettingsToolKey))
        {
            ConsoleLog.Append("[Orbit] Settings tool is unavailable; falling back to legacy view.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
            OpenOrFocusToolTab(
                key: SettingsToolKey,
                name: "Settings",
                controlFactory: () => ResolveRequiredService<Views.SettingsView>(),
                icon: PackIconMaterialKind.Cog);
        }
    }

    public void OpenConsoleTab()
    {
        if (!TryOpenToolByKey(ConsoleToolKey))
        {
            ConsoleLog.Append("[Orbit] Console tool is unavailable; falling back to legacy view.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
            OpenOrFocusToolTab(
                key: ConsoleToolKey,
                name: "Console",
                controlFactory: () => new Views.ConsoleView(),
                icon: PackIconMaterialKind.Console);
        }
    }

    public void OpenThemeManagerTab()
    {
        if (!TryOpenToolByKey(ThemeManagerToolKey))
        {
            ConsoleLog.Append("[Orbit] Theme Manager tool is unavailable; falling back to legacy view.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
            OpenOrFocusToolTab(
                key: ThemeManagerToolKey,
                name: "Theme Manager",
                controlFactory: () => ResolveRequiredService<Views.ThemeManagerPanel>(),
                icon: PackIconMaterialKind.Palette);
        }
    }

    public void OpenSessionsOverviewTab()
    {
        if (!TryOpenToolByKey(SessionsOverviewToolKey))
        {
            ConsoleLog.Append("[Orbit] Sessions tool is unavailable.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
        }
    }

	public void OpenGuideTab()
	{
		if (!TryOpenToolByKey(GuideToolKey))
		{
			ConsoleLog.Append("[Orbit] Guide tool is unavailable.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
		}
	}

	public void OpenToolsOverviewTab()
	{
		if (!TryOpenToolByKey(ToolsOverviewToolKey))
		{
			ConsoleLog.Append("[Orbit] Tools dashboard is unavailable.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
		}
	}

	private bool TryOpenToolByKey(string key)
	{
		var tool = toolRegistry.Find(key);
		if (tool == null)
		{
			return false;
		}

		OpenOrFocusToolTab(tool.Key, tool.DisplayName, () => tool.CreateView(this), tool.Icon);
		return true;
	}

	private static T ResolveRequiredService<T>() where T : class
	{
		var app = Application.Current as App;
		var service = app?.Services.GetService<T>();
		if (service == null)
		{
			throw new InvalidOperationException($"Required service '{typeof(T).Name}' is not available from DI.");
		}

		return service;
	}

	/// <summary>
	/// Ensures the requested tool has a tab in the current window. If a tab already exists we focus it; otherwise
	/// we create a new host control and wire the main view model as DataContext.
	/// </summary>
	private void OpenOrFocusToolTab(string key, string name, Func<System.Windows.FrameworkElement> controlFactory, PackIconMaterialKind icon = PackIconMaterialKind.Tools)
    {
        // Find existing
        foreach (var item in Tabs)
        {
            if (item is Models.ToolTabItem tool && string.Equals(tool.Key, key, StringComparison.Ordinal))
            {
                SelectedTab = tool;
                return;
            }
        }

        if (TryAdoptToolFromOtherWindow(key, out var adoptedTool))
        {
	        Tabs.Add(adoptedTool);
	        SelectedTab = adoptedTool;
	        return;
        }

        var ctrl = controlFactory();
        if (ctrl.DataContext == null)
        {
            // Most tool tabs bind to the main window VM; set if not already set by control
            ctrl.DataContext = this;
        }
        var newTool = new Models.ToolTabItem(key, name, ctrl, icon);
        Tabs.Add(newTool);
        SelectedTab = newTool;
    }

	/// <summary>
	/// Tries to reuse a tool tab that currently lives in another Orbit window instead of spinning up a duplicate
	/// instance. This keeps singleton tooling (theme manager, console, etc.) consistent across tear-off windows.
	/// </summary>
	private bool TryAdoptToolFromOtherWindow(string key, out Models.ToolTabItem? adopted)
	{
		adopted = null;
		if (string.Equals(key, "OrbitView", StringComparison.Ordinal))
		{
			// Orbit View carries window-specific callbacks in its view model.
			// Recreate it per window rather than adopting a live instance from another shell.
			return false;
		}

		var windows = Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel otherVm || ReferenceEquals(otherVm, this))
			{
				continue;
			}

			var existingTool = otherVm.Tabs.OfType<Models.ToolTabItem>()
				.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));
			if (existingTool == null)
			{
				continue;
			}

			otherVm.Tabs.Remove(existingTool);
			if (ReferenceEquals(otherVm.SelectedTab, existingTool))
			{
				otherVm.SelectedTab = otherVm.Tabs.FirstOrDefault();
			}

			if (existingTool.HostControl != null && ReferenceEquals(existingTool.HostControl.DataContext, otherVm))
			{
				existingTool.HostControl.DataContext = this;
			}

			adopted = existingTool;
			return true;
		}

		return false;
	}

	private bool CanMoveTabToOrbit(object parameter)
	{
		var target = parameter ?? SelectedTab;
		if (target is SessionModel session)
		{
			return session != null;
		}

		if (target is Models.ToolTabItem tool)
		{
			if (string.Equals(tool.Key, "OrbitView", StringComparison.Ordinal))
			{
				return false;
			}

			return !orbitLayoutState.Items.Contains(tool);
		}

		return false;
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
			// Ensure the session isn't simultaneously shown in other session surfaces.
			if (Tabs.Contains(session))
			{
				Tabs.Remove(session);
				HandleTabRemoval(session);
			}

			orbitLayoutState.AddItem(session);
			SelectedSession = session;
		}
		else if (target is Models.ToolTabItem tool)
		{
			if (Tabs.Contains(tool))
			{
				Tabs.Remove(tool);
				HandleTabRemoval(tool);
			}

			if (tool.HostControl != null && !ReferenceEquals(tool.HostControl.DataContext, this))
			{
				tool.HostControl.DataContext = this;
			}

			orbitLayoutState.AddItem(tool);
		}

		TryOpenToolByKey("OrbitView");
		var orbitTab = Tabs.OfType<Models.ToolTabItem>()
			.FirstOrDefault(t => string.Equals(t.Key, "OrbitView", StringComparison.Ordinal));
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
		return target is SessionModel session && session != null && !Tabs.Contains(session);
	}

	private void MoveSessionToIndividualTabs(object parameter)
	{
		var target = parameter ?? SelectedSession;
		if (target is not SessionModel session)
		{
			return;
		}

		orbitLayoutState.RemoveItem(session);

		if (!Tabs.Contains(session))
		{
			Tabs.Add(session);
		}

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
			? OppositeToPlacement(FloatingMenuActiveSide)
			: ToPlacement(FloatingMenuDirection);

	public FloatingMenuDirection FloatingMenuExpansionDirection
		=> FloatingMenuAutoDirection
			? OppositeOf(FloatingMenuActiveSide)
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
	{
		get
		{
			var cornerWidth = Math.Clamp(Settings.Default.FloatingMenuDockCornerThreshold, 60d, 250d);
			var cornerHeight = Math.Clamp(Settings.Default.FloatingMenuDockCornerHeight, 60d, 250d);
			var extent = Math.Min(cornerWidth, cornerHeight);
			var roundness = Math.Clamp(Settings.Default.FloatingMenuDockCornerRoundness, 0d, 1d);
			var radius = Math.Max(0d, extent * roundness);
			return new CornerRadius(radius);
		}
	}

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

	public bool ShowMenuFsmNodeEditor
	{
		get => Settings.Default.ShowMenuFsmNodeEditor;
		set
		{
			if (Settings.Default.ShowMenuFsmNodeEditor == value) return;
			Settings.Default.ShowMenuFsmNodeEditor = value;
			OnPropertyChanged(nameof(ShowMenuFsmNodeEditor));
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
		if (SelectedTab == null)
		{
			if (Tabs.Count == 0)
			{
				if (ShowFloatingMenuOnHome || ShowFloatingMenuOnSessionTabs || ShowFloatingMenuOnToolTabs)
				{
					IsFloatingMenuVisible = true;
				}
				else
				{
					HideFloatingMenu();
				}
				return;
			}

			if (ShowFloatingMenuOnSessionTabs || ShowFloatingMenuOnToolTabs)
			{
				IsFloatingMenuVisible = true;
			}
			else
			{
				HideFloatingMenu();
			}
			return;
		}

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

		const double edgeMargin = 24d;

		var usableWidth = Math.Max(0, hostViewportWidth - handleWidth);
		var usableHeight = Math.Max(0, hostViewportHeight - handleHeight);
		var centerLeft = usableWidth / 2;
		var centerTop = usableHeight / 2;
		var leftEdge = Math.Max(0, edgeMargin);
		var topEdge = Math.Max(0, edgeMargin);
		var rightEdge = Math.Max(0, hostViewportWidth - handleWidth - edgeMargin);
		var bottomEdge = Math.Max(0, hostViewportHeight - handleHeight - edgeMargin);

		double newLeft = FloatingMenuLeft;
		double newTop = FloatingMenuTop;

		switch (region)
		{
			case FloatingMenuDockRegion.Left:
				newLeft = leftEdge;
				newTop = centerTop;
				break;
			case FloatingMenuDockRegion.Right:
				newLeft = rightEdge;
				newTop = centerTop;
				break;
			case FloatingMenuDockRegion.Top:
				newLeft = centerLeft;
				newTop = topEdge;
				break;
			case FloatingMenuDockRegion.Bottom:
				newLeft = centerLeft;
				newTop = bottomEdge;
				break;
			case FloatingMenuDockRegion.TopLeft:
				newLeft = leftEdge;
				newTop = topEdge;
				break;
			case FloatingMenuDockRegion.TopRight:
				newLeft = rightEdge;
				newTop = topEdge;
				break;
			case FloatingMenuDockRegion.BottomLeft:
				newLeft = leftEdge;
				newTop = bottomEdge;
				break;
			case FloatingMenuDockRegion.BottomRight:
				newLeft = rightEdge;
				newTop = bottomEdge;
				break;
			case FloatingMenuDockRegion.Center:
				newLeft = centerLeft;
				newTop = centerTop;
				break;
		}

		FloatingMenuLeft = Math.Clamp(newLeft, 0, usableWidth);
		FloatingMenuTop = Math.Clamp(newTop, 0, usableHeight);

		ComputeAutoActiveSide();

		if (!FloatingMenuAutoDirection)
		{
			FloatingMenuDirection = DetermineDirectionForRegion(region);
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

	private static FloatingMenuDirection DetermineDirectionForRegion(FloatingMenuDockRegion region)
	{
		return region switch
		{
			FloatingMenuDockRegion.Left => FloatingMenuDirection.Right,
			FloatingMenuDockRegion.Right => FloatingMenuDirection.Left,
			FloatingMenuDockRegion.Top => FloatingMenuDirection.Down,
			FloatingMenuDockRegion.Bottom => FloatingMenuDirection.Up,
			FloatingMenuDockRegion.TopLeft => FloatingMenuDirection.Right,
			FloatingMenuDockRegion.TopRight => FloatingMenuDirection.Left,
			FloatingMenuDockRegion.BottomLeft => FloatingMenuDirection.Right,
			FloatingMenuDockRegion.BottomRight => FloatingMenuDirection.Left,
			_ => FloatingMenuDirection.Right
		};
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
		if (SelectedTab == null)
		{
			if (Tabs.Count == 0)
			{
				return ShowFloatingMenuOnHome || ShowFloatingMenuOnSessionTabs || ShowFloatingMenuOnToolTabs;
			}

			return ShowFloatingMenuOnSessionTabs || ShowFloatingMenuOnToolTabs;
		}

		return SelectedTab is SessionModel
			? ShowFloatingMenuOnSessionTabs
			: ShowFloatingMenuOnToolTabs;
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
		if (hostViewportWidth <= 0 || hostViewportHeight <= 0)
			return;

		const double handleRadius = 24;
		var centerX = FloatingMenuLeft + handleRadius;
		var centerY = FloatingMenuTop + handleRadius;

		var distances = new Dictionary<FloatingMenuDirection, double>
		{
			{ FloatingMenuDirection.Left, Math.Max(0, centerX) },
			{ FloatingMenuDirection.Right, Math.Max(0, hostViewportWidth - centerX) },
			{ FloatingMenuDirection.Up, Math.Max(0, centerY) },
			{ FloatingMenuDirection.Down, Math.Max(0, hostViewportHeight - centerY) }
		};

		var best = FloatingMenuDirection;
		var bestDistance = double.MaxValue;
		foreach (var pair in distances)
		{
			if (pair.Value < bestDistance)
			{
				bestDistance = pair.Value;
				best = pair.Key;
			}
		}

		autoActiveSide = best;
		OnPropertyChanged(nameof(FloatingMenuActiveSide));
		OnPropertyChanged(nameof(FloatingMenuExpansionDirection));
		OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
		OnPropertyChanged(nameof(FloatingMenuPopupHorizontalOffset));
		OnPropertyChanged(nameof(FloatingMenuPopupVerticalOffset));
	}

	private static PlacementMode ToPlacement(FloatingMenuDirection side) => side switch
	{
		FloatingMenuDirection.Left => PlacementMode.Left,
		FloatingMenuDirection.Right => PlacementMode.Right,
		FloatingMenuDirection.Up => PlacementMode.Top,
		FloatingMenuDirection.Down => PlacementMode.Bottom,
		_ => PlacementMode.Right
	};

	private static PlacementMode OppositeToPlacement(FloatingMenuDirection side) => side switch
	{
		FloatingMenuDirection.Left => PlacementMode.Right,
		FloatingMenuDirection.Right => PlacementMode.Left,
		FloatingMenuDirection.Up => PlacementMode.Bottom,
		FloatingMenuDirection.Down => PlacementMode.Top,
		_ => PlacementMode.Right
	};

	private static FloatingMenuDirection OppositeOf(FloatingMenuDirection side) => side switch
	{
		FloatingMenuDirection.Left => FloatingMenuDirection.Right,
		FloatingMenuDirection.Right => FloatingMenuDirection.Left,
		FloatingMenuDirection.Up => FloatingMenuDirection.Down,
		FloatingMenuDirection.Down => FloatingMenuDirection.Up,
		_ => FloatingMenuDirection.Right
	};

		private async Task LoadScriptAsync()
		{
			if (!CanLoadScript())
			{
				return;
			}

			var path = hotReloadScriptPath;
			if (!File.Exists(path))
			{
				ConsoleLog.Append($"[OrbitCmd] Script not found: {path}", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
				return;
			}

			// Track script usage
			ScriptManager.AddOrUpdateScript(path);

		var targetSession = ResolveHotReloadTarget();
		if (targetSession == null)
		{
			ConsoleLog.Append("[OrbitCmd] No active session available for load.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return;
		}

		if (targetSession.InjectionState != InjectionState.Injected)
		{
			ConsoleLog.Append($"[OrbitCmd] Session '{targetSession.Name}' is not injected; load aborted.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return;
		}

		var activePath = targetSession.ActiveScriptPath;
		var switchingScripts = !string.IsNullOrWhiteSpace(activePath) &&
			!string.Equals(activePath, path, StringComparison.OrdinalIgnoreCase);
		if (switchingScripts)
		{
			var unloaded = await TryUnloadActiveScriptBeforeLoadAsync(targetSession, path).ConfigureAwait(false);
			if (!unloaded)
			{
				targetSession.SetScriptRuntimeError("Failed to unload previous script before loading a new one.");
				return;
			}
		}

		ConsoleLog.Append($"[OrbitCmd] Requesting load for '{path}' (session '{targetSession.Name}' PID {targetSession.RSProcess?.Id})", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
		// Use RELOAD for both initial and subsequent loads to avoid legacy runtime restart; send to selected session
		var runtimeReady = await OrbitCommandClient
			.SendStartRuntimeWithRetryAsync(targetSession.RSProcess?.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);
		if (!runtimeReady)
		{
			ConsoleLog.Append($"[OrbitCmd] Unable to start ME .NET runtime for '{targetSession.Name}'. Load may fail.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
		}

		var success = targetSession.RSProcess != null
			? await OrbitCommandClient.SendReloadWithRetryAsync(path, targetSession.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
			: await OrbitCommandClient.SendReloadWithRetryAsync(path, null, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None);
			if (!success)
			{
				ConsoleLog.Append("[OrbitCmd] Failed to send load command.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			}
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
				return;
			}

			var path = hotReloadScriptPath;
			if (!File.Exists(path))
			{
				ConsoleLog.Append($"[OrbitCmd] Script not found: {path}", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
				return;
			}

		// Track script usage
		ScriptManager.AddOrUpdateScript(path);

		var targetSession = ResolveHotReloadTarget();
		if (targetSession == null)
		{
			ConsoleLog.Append("[OrbitCmd] No active session available for reload.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return;
		}

		if (targetSession.InjectionState != InjectionState.Injected)
		{
			ConsoleLog.Append($"[OrbitCmd] Session '{targetSession.Name}' is not injected; reload aborted.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			return;
		}

		var activePath = targetSession.ActiveScriptPath;
		var switchingScripts = !string.IsNullOrWhiteSpace(activePath) &&
			!string.Equals(activePath, path, StringComparison.OrdinalIgnoreCase);
		if (switchingScripts)
		{
			var unloaded = await TryUnloadActiveScriptBeforeLoadAsync(targetSession, path).ConfigureAwait(false);
			if (!unloaded)
			{
				targetSession.SetScriptRuntimeError("Failed to unload previous script before reloading a new one.");
				return;
			}
		}

		ConsoleLog.Append($"[OrbitCmd] Requesting reload for '{path}' (session '{targetSession.Name}' PID {targetSession.RSProcess?.Id})", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
		var runtimeReady = await OrbitCommandClient
			.SendStartRuntimeWithRetryAsync(targetSession.RSProcess?.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);
		if (!runtimeReady)
		{
			ConsoleLog.Append($"[OrbitCmd] Unable to start ME .NET runtime for '{targetSession.Name}'. Reload may fail.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
		}

		var success = targetSession.RSProcess != null
			? await OrbitCommandClient.SendReloadWithRetryAsync(path, targetSession.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
			: await OrbitCommandClient.SendReloadWithRetryAsync(path, null, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None);
			if (!success)
			{
				ConsoleLog.Append("[OrbitCmd] Failed to send reload command.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			}
		}

	private async Task<bool> TryUnloadActiveScriptBeforeLoadAsync(SessionModel targetSession, string nextPath)
	{
		if (targetSession.RSProcess == null)
		{
			return false;
		}

		ConsoleLog.Append(
			$"[OrbitCmd] Unloading active script '{targetSession.ActiveScriptName}' before loading '{Path.GetFileNameWithoutExtension(nextPath)}' in session '{targetSession.Name}'.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		var unloaded = await OrbitCommandClient
			.SendUnloadScriptWithRetryAsync(targetSession.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
			.ConfigureAwait(false);

		if (!unloaded)
		{
			ConsoleLog.Append(
				$"[OrbitCmd] Failed to unload previous script in session '{targetSession.Name}'.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return false;
		}

		targetSession.SetScriptStopped();
		await Task.Delay(250).ConfigureAwait(false);
		return true;
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

		SelectedSession = session;
	}

	/// <summary>
	/// Reasserts MESharp input passthrough and focus spoof state for injected sessions.
	/// This mirrors the WPF debug app behavior so Orbit can reliably accept keyboard input.
	/// </summary>
	public Task ReassertInputPassthroughAsync(bool orbitActive)
	{
		var targets = Sessions
			.OfType<SessionModel>()
			.Where(s => s.InjectionState == InjectionState.Injected && s.RSProcess != null && !s.RSProcess.HasExited)
			.ToList();

		if (targets.Count == 0)
		{
			return Task.CompletedTask;
		}

		var focusSpoofEnabled = false;

		return Task.Run(async () =>
		{
			foreach (var session in targets)
			{
				try
				{
					var process = session.RSProcess;
					if (process == null || process.HasExited)
					{
						continue;
					}

					await OrbitCommandClient
						.SendInputModeWithRetryAsync(1, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
						.ConfigureAwait(false);

					await OrbitCommandClient
						.SendDebugMenuVisibleWithRetryAsync(false, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
						.ConfigureAwait(false);

					await OrbitCommandClient
						.SendFocusSpoofWithRetryAsync(focusSpoofEnabled, process.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(100))
						.ConfigureAwait(false);
				}
				catch
				{
					// Best-effort only; individual sessions may be shutting down.
				}
			}
		});
	}

	public void FocusSession(SessionModel session)
	{
		session?.SetFocus();
	}

	public void CloseSession(SessionModel session)
	{
		if (session == null)
			return;

		_ = CloseSessionInternalAsync(session, skipConfirmation: false, forceKillOnTimeout: false);
	}

		public async Task CloseAllSessionsAsync(bool skipConfirmation, bool forceKillOnTimeout = false)
		{
			var sessionsSnapshot = Sessions.OfType<SessionModel>().ToList();
			foreach (var session in sessionsSnapshot)
			{
				await CloseSessionInternalAsync(session, skipConfirmation, forceKillOnTimeout).ConfigureAwait(true);
			}
		}

		public Task ShutdownTrackedProcessesAsync(bool forceKillOnTimeout = false, CancellationToken cancellationToken = default)
			=> sessionManager.ShutdownManagedProcessesAsync(forceKillOnTimeout, cancellationToken);

		private async Task CloseSessionInternalAsync(SessionModel session, bool skipConfirmation, bool forceKillOnTimeout = false)
		{
			if (session == null)
			{
				return;
			}

			if (!skipConfirmation)
			{
				var confirmed = await ConfirmSessionCloseAsync(session).ConfigureAwait(true);
				if (!confirmed)
				{
					return;
				}
			}

			try
			{
				var pid = session.RSProcess?.Id;
				ConsoleLog.Append(
					$"[Orbit] Requesting shutdown for session '{session.Name}'{(pid.HasValue ? $" (PID {pid.Value})" : string.Empty)}.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				await sessionManager.ShutdownSessionAsync(session, forceKillOnTimeout: forceKillOnTimeout).ConfigureAwait(true);
			}
			catch (Exception ex)
			{
				ConsoleLog.Append(
					$"[Orbit] Failed to shutdown session '{session.Name}': {ex.Message}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Error);
			}
			finally
			{
				// Ensure the session is removed from any Orbit View workspace state (no-op if not present).
				orbitLayoutState.RemoveItem(session);

				if (Sessions.Contains(session))
				{
					Sessions.Remove(session);
				}

				if (Tabs.Contains(session))
				{
					Tabs.Remove(session);
				}

				HandleTabRemoval(session);
			}
		}

		private static bool ShouldConfirmSessionClose(SessionModel session)
		{
			if (session == null || !session.IsRuneScapeClient)
			{
				return false;
			}

			return session.State == SessionState.ClientReady ||
				   session.State == SessionState.Injecting ||
				   session.State == SessionState.Injected ||
				   session.InjectionState == InjectionState.Injected;
		}

	private async Task<bool> ConfirmSessionCloseAsync(SessionModel session)
	{
		if (!ShouldConfirmSessionClose(session))
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
			return dialog.DialogResult;
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

		if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
		{
			if (Tabs.Count == 0)
			{
				SelectedTab = null;
				SelectedSession = null;
			}
			else if (SelectedTab == null || (e.OldItems != null && e.OldItems.Contains(SelectedTab)))
			{
				SelectedTab = Tabs.FirstOrDefault();
			}
		}
		else if (e.Action == NotifyCollectionChangedAction.Replace && e.OldItems != null && e.OldItems.Contains(SelectedTab))
		{
			SelectedTab = e.NewItems?.Cast<object>().FirstOrDefault() ?? Tabs.FirstOrDefault();
		}

		UpdateFloatingMenuVisibilityForCurrentTab();
	}

		private void HandleTabRemoval(object removedItem)
		{
		if (Tabs.Count == 0)
		{
				SelectedTab = null;
				SelectedSession = null;
				UpdateFloatingMenuVisibilityForCurrentTab();
				return;
			}

			if (SelectedTab == null || (removedItem != null && ReferenceEquals(removedItem, SelectedTab)))
			{
				SelectedTab = Tabs[0];
			}

				UpdateFloatingMenuVisibilityForCurrentTab();
			}

		private static void DisposeToolItem(object? item)
		{
			if (item is not Models.ToolTabItem tool || tool.HostControl == null)
			{
				return;
			}

			try
			{
				if (tool.HostControl.DataContext is IDisposable disposable &&
					disposable is not MainWindowViewModel)
				{
					disposable.Dispose();
				}
			}
			catch
			{
				// Best effort cleanup.
			}
		}

		private void RestoreOrbitWorkspaceToTabs()
		{
			var workspaceItems = orbitLayoutState.Items.ToList();
			if (workspaceItems.Count == 0)
			{
				return;
			}

			foreach (var item in workspaceItems)
			{
				switch (item)
				{
					case SessionModel session:
						orbitLayoutState.RemoveItem(session);
						if (!Tabs.Contains(session))
						{
							Tabs.Add(session);
						}
						break;
					case Models.ToolTabItem tool:
						if (string.Equals(tool.Key, "OrbitView", StringComparison.Ordinal))
						{
							continue;
						}

						orbitLayoutState.RemoveItem(tool);
						if (tool.HostControl != null && !ReferenceEquals(tool.HostControl.DataContext, this))
						{
							tool.HostControl.DataContext = this;
						}
						if (!Tabs.Contains(tool))
						{
							Tabs.Add(tool);
						}
						break;
				}
			}
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
				}
			}

		if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
		{
			if (hotReloadTargetSession != null && !Sessions.Contains(hotReloadTargetSession))
			{
				HotReloadTargetSession = Sessions.FirstOrDefault();
			}
		}
		else if ((e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace) && HotReloadTargetSession == null)
		{
			HotReloadTargetSession = Sessions.FirstOrDefault();
		}

		if (SelectedSession != null && !Sessions.Contains(SelectedSession))
		{
			SelectedSession = Sessions.FirstOrDefault();
		}
		else if (SelectedSession == null && Sessions.Count > 0 && (e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace))
		{
			SelectedSession = Sessions.FirstOrDefault();
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
