using Dragablz;
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
using FlowDirection = System.Windows.FlowDirection;

namespace Orbit.ViewModels
{
	public class MainWindowViewModel : INotifyPropertyChanged
	{
	private const string AccountManagerToolKey = "AccountManager";
	private const string ScriptControlsToolKey = "ScriptControls";
	private const string SettingsToolKey = "Settings";
	private const string ConsoleToolKey = "Console";
	private const string ThemeManagerToolKey = "ThemeManager";
	private const string SessionsOverviewToolKey = "SessionsOverview";
	private const string ScriptManagerToolKey = "ScriptManager";
	private const string ApiDocumentationToolKey = "ApiDocumentation";
	private const string ToolsOverviewToolKey = "ToolsOverview";

	private readonly SessionManagerService sessionManager;
	private readonly ThemeService themeService;
	private readonly ScriptIntegrationService scriptIntegrationService;
	private readonly ScriptManagerService scriptManagerService;
	private readonly AccountService accountService;
	private readonly AutoLoginService autoLoginService;
	private readonly SessionCollectionService sessionCollectionService;
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
	private FloatingMenuDirection floatingMenuDirection;
	private FloatingMenuDirection autoActiveSide;
	private double floatingMenuInactivitySeconds;
	private bool floatingMenuAutoDirection;
	private bool isFloatingMenuDockOverlayVisible;
	private FloatingMenuDockRegion floatingMenuDockCandidate = FloatingMenuDockRegion.None;
	private double hostViewportWidth = 1200;
	private double hostViewportHeight = 900;
	private bool autoInjectOnReady;
	private bool isFloatingMenuClipping;

	public MainWindowViewModel(
		SessionManagerService sessionManager,
		ThemeService themeService,
		ScriptIntegrationService scriptIntegrationService,
		ScriptManagerService scriptManagerService,
		AccountService accountService,
		AutoLoginService autoLoginService,
		SessionCollectionService sessionCollectionService,
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
		this.consoleLogService = consoleLogService ?? throw new ArgumentNullException(nameof(consoleLogService));
		this.interTabClient = interTabClient ?? throw new ArgumentNullException(nameof(interTabClient));
		this.toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));

		ScriptManager = this.scriptManagerService;
		AccountService = this.accountService;
		ConsoleLog = this.consoleLogService;

		Sessions = this.sessionCollectionService.Sessions;
		Tabs = new ObservableCollection<object>();
		Sessions.CollectionChanged += OnSessionsCollectionChanged;
		foreach (var session in Sessions)
		{
			session.PropertyChanged += OnSessionPropertyChanged;
		}

		selectedSession = this.sessionCollectionService.GlobalSelectedSession;
		this.sessionCollectionService.PropertyChanged += OnGlobalSessionChanged;

		InterTabClient = this.interTabClient;
		hotReloadScriptPath = Settings.Default.HotReloadScriptPath ?? string.Empty;

		AddSessionCommand = new RelayCommand(async _ => await AddSessionAsync());
		InjectCommand = new RelayCommand(async _ => await InjectAsync(), _ => CanInject());
		ShowSessionsCommand = new RelayCommand(_ => ShowSessions(), _ => Sessions.Count > 0);
		OpenThemeManagerCommand = new RelayCommand(_ => OpenThemeManager());
		OpenScriptManagerCommand = new RelayCommand(_ => OpenScriptManager());
		OpenAccountManagerCommand = new RelayCommand(_ => OpenAccountManager(), _ => this.toolRegistry.Find(AccountManagerToolKey) != null);
		OpenApiDocumentationCommand = new RelayCommand(_ => OpenApiDocumentationTab());
		OpenSettingsCommand = new RelayCommand(_ => OpenSettingsTab());
		OpenToolsOverviewCommand = new RelayCommand(_ => OpenToolsOverviewTab());
		ToggleConsoleCommand = new RelayCommand(_ => ToggleConsole());
		BrowseScriptCommand = new RelayCommand(_ => BrowseForScript());
		LoadScriptCommand = new RelayCommand(async _ => await LoadScriptAsync(), _ => CanLoadScript());
		ReloadScriptCommand = new RelayCommand(async _ => await ReloadScriptAsync(), _ => CanReloadScript());
		BeginSessionRenameCommand = new RelayCommand(BeginSessionRename, parameter => parameter is SessionModel);
		CommitSessionRenameCommand = new RelayCommand(CommitSessionRename, parameter => parameter is SessionModel);
		CancelSessionRenameCommand = new RelayCommand(CancelSessionRename, parameter => parameter is SessionModel);

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
		HotReloadTargetSession = selectedSession ?? Sessions.FirstOrDefault();

		ApplyThemeFromSettings();
	}

	public ObservableCollection<SessionModel> Sessions { get; }
	public ObservableCollection<object> Tabs { get; }
	public IInterTabClient InterTabClient { get; }
	public bool HasSessions => Sessions.Count > 0;
	public ScriptManagerService ScriptManager { get; }
	public ScriptIntegrationService ScriptIntegration => scriptIntegrationService;
	public ICommand AddSessionCommand { get; }
	public ICommand InjectCommand { get; }
	public ICommand ShowSessionsCommand { get; }
	public ICommand OpenThemeManagerCommand { get; }
	public ICommand OpenScriptManagerCommand { get; }
	public ICommand ToggleConsoleCommand { get; }
	public ICommand BrowseScriptCommand { get; }
	public ICommand LoadScriptCommand { get; }
	public ICommand ReloadScriptCommand { get; }
	public ICommand BeginSessionRenameCommand { get; }
	public ICommand CommitSessionRenameCommand { get; }
	public ICommand CancelSessionRenameCommand { get; }
	public ConsoleLogService ConsoleLog { get; }
	public AccountService AccountService { get; }
	public ICommand OpenAccountManagerCommand { get; }
	public ICommand OpenApiDocumentationCommand { get; }
	public ICommand OpenSettingsCommand { get; }
	public ICommand OpenToolsOverviewCommand { get; }
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

		public ICommand OpenThemeLogCommand => new RelayCommand(_ =>
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

		public ICommand ClearThemeLogCommand => new RelayCommand(_ =>
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

		private async Task AddSessionAsync()
		{
			var session = new SessionModel
			{
				Id = Guid.NewGuid(),
				Name = $"RuneScape Session {Sessions.Count + 1}",
				CreatedAt = DateTime.Now,
				HostControl = new ChildClientView(),
				RequireInjectionBeforeDock = AutoInjectOnReady
			};

			Sessions.Add(session);
			Tabs.Add(session);
			SelectedSession = session;
			SelectedTab = session; // Auto-focus the new tab

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
			controlFactory: () => new Views.ScriptManagerPanel(new ScriptManagerViewModel(scriptManagerService)),
			icon: PackIconMaterialKind.CodeBraces);
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
				OnPropertyChanged(nameof(HotReloadTargetSession));
				CommandManager.InvalidateRequerySuggested();
			}
		}

	public void TabControl_ClosingItemHandler(ItemActionCallbackArgs<TabablzControl> args)
	{
		if (args.DragablzItem.DataContext is SessionModel session)
		{
			args.Cancel();
			_ = CloseSessionInternalAsync(session, skipConfirmation: false);
			return;
		}

		var removedItem = args.DragablzItem.DataContext;
		Tabs.Remove(removedItem);
		HandleTabRemoval(removedItem);
	}

		private bool CanInject() => SelectedSession?.IsInjectable == true;

	private SessionModel? ResolveHotReloadTarget()
	{
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

	public void OpenScriptControlsTab()
	{
		if (!TryOpenToolByKey(ScriptControlsToolKey))
		{
			ConsoleLog.Append("[Orbit] Script Controls tool is unavailable; falling back to legacy view.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			OpenOrFocusToolTab(
				key: ScriptControlsToolKey,
				name: "Script Controls",
				controlFactory: () => new Views.ScriptControlsView(),
				icon: PackIconMaterialKind.ScriptText);
		}
	}

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
                controlFactory: () => new Views.SettingsView(),
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
                controlFactory: () => new Views.ThemeManagerPanel(),
                icon: PackIconMaterialKind.Palette);
        }
    }

    public void OpenSessionsOverviewTab()
    {
        if (!TryOpenToolByKey(SessionsOverviewToolKey))
        {
            OpenOrFocusToolTab(
                key: SessionsOverviewToolKey,
                name: "Sessions",
                controlFactory: () => new Views.SessionsOverviewView(
                    new SessionsOverviewViewModel(Sessions, ActivateSession, FocusSession, CloseSession)),
                icon: PackIconMaterialKind.ViewList);
        }
    }

	public void OpenApiDocumentationTab()
	{
		if (!TryOpenToolByKey(ApiDocumentationToolKey))
		{
			ConsoleLog.Append("[Orbit] API Documentation tool is unavailable.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
		}
	}

	public void OpenToolsOverviewTab()
	{
		if (!TryOpenToolByKey(ToolsOverviewToolKey))
		{
			ConsoleLog.Append("[Orbit] Tools Overview tool is unavailable.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
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

	#endregion

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

	public bool IsFloatingMenuExpanded
	{
		get => isFloatingMenuExpanded;
		set
		{
			if (isFloatingMenuExpanded == value)
				return;
			isFloatingMenuExpanded = value;
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

	public bool ShowMenuApiDocumentation
	{
		get => Settings.Default.ShowMenuApiDocumentation;
		set
		{
			if (Settings.Default.ShowMenuApiDocumentation == value) return;
			Settings.Default.ShowMenuApiDocumentation = value;
			OnPropertyChanged(nameof(ShowMenuApiDocumentation));
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

		ConsoleLog.Append($"[OrbitCmd] Requesting load for '{path}' (session '{targetSession.Name}' PID {targetSession.RSProcess?.Id})", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
		// Use RELOAD for both initial and subsequent loads to avoid legacy runtime restart; send to selected session
		var success = targetSession.RSProcess != null
			? await OrbitCommandClient.SendReloadAsync(path, targetSession.RSProcess.Id, CancellationToken.None)
			: await OrbitCommandClient.SendReloadAsync(path, CancellationToken.None);
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

		ConsoleLog.Append($"[OrbitCmd] Requesting reload for '{path}' (session '{targetSession.Name}' PID {targetSession.RSProcess?.Id})", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
		var success = targetSession.RSProcess != null
			? await OrbitCommandClient.SendReloadAsync(path, targetSession.RSProcess.Id, CancellationToken.None)
			: await OrbitCommandClient.SendReloadAsync(path, CancellationToken.None);
			if (!success)
			{
				ConsoleLog.Append("[OrbitCmd] Failed to send reload command.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
			}
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

	public void FocusSession(SessionModel session)
	{
		session?.SetFocus();
	}

	public void CloseSession(SessionModel session)
	{
		if (session == null)
			return;

		_ = CloseSessionInternalAsync(session, skipConfirmation: false);
	}

		public async Task CloseAllSessionsAsync(bool skipConfirmation, bool forceKillOnTimeout = false)
		{
			var sessionsSnapshot = Sessions.OfType<SessionModel>().ToList();
			foreach (var session in sessionsSnapshot)
			{
				await CloseSessionInternalAsync(session, skipConfirmation, forceKillOnTimeout).ConfigureAwait(true);
			}
		}

		private async Task CloseSessionInternalAsync(SessionModel session, bool skipConfirmation, bool forceKillOnTimeout = false)
		{
			if (session == null)
			{
				return;
			}

			if (!skipConfirmation && ShouldConfirmSessionClose(session))
			{
				var result = MessageBox.Show(
					$"Session '{session.Name}' is active. Are you sure you want to close it?",
					"Close Active Session",
					MessageBoxButton.YesNo,
					MessageBoxImage.Warning);

				if (result != MessageBoxResult.Yes)
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

		OnPropertyChanged(nameof(HasSessions));
		CommandManager.InvalidateRequerySuggested();
	}

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
