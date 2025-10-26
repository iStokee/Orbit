using Dragablz;
using Microsoft.Win32;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using Orbit.Views;
using Orbit;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using MahApps.Metro.IconPacks;

namespace Orbit.ViewModels
{
	public class MainWindowViewModel : INotifyPropertyChanged
	{
		private readonly SessionManagerService sessionManager;
		private readonly ThemeService themeService;
		private readonly ScriptIntegrationService scriptIntegrationService;
		private SessionModel selectedSession;
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

	public MainWindowViewModel()
	{
		sessionManager = new SessionManagerService();
		themeService = new ThemeService();
		scriptIntegrationService = new ScriptIntegrationService(SessionCollectionService.Instance);
		ScriptManager = new ScriptManagerService();
		AccountService = new AccountService();

			// Use shared singleton Sessions collection so all windows can access the same sessions
			Sessions = SessionCollectionService.Instance.Sessions;
			Tabs = new ObservableCollection<object>();
			Sessions.CollectionChanged += OnSessionsCollectionChanged;

		// Sync with global selected session (important for tear-off windows)
		selectedSession = SessionCollectionService.Instance.GlobalSelectedSession;
		SessionCollectionService.Instance.PropertyChanged += OnGlobalSessionChanged;

		InterTabClient = new InterTabClient();
		hotReloadScriptPath = Settings.Default.HotReloadScriptPath ?? string.Empty;

		AddSessionCommand = new RelayCommand(async _ => await AddSessionAsync());
		InjectCommand = new RelayCommand(async _ => await InjectAsync(), _ => CanInject());
		ShowSessionsCommand = new RelayCommand(_ => ShowSessions(), _ => Sessions.Count > 0);
		OpenThemeManagerCommand = new RelayCommand(_ => OpenThemeManager());
		OpenScriptManagerCommand = new RelayCommand(_ => OpenScriptManager());
		OpenAccountManagerCommand = new RelayCommand(_ => OpenAccountManager());
		ToggleConsoleCommand = new RelayCommand(_ => ToggleConsole());
		BrowseScriptCommand = new RelayCommand(_ => BrowseForScript());
		LoadScriptCommand = new RelayCommand(async _ => await LoadScriptAsync(), _ => CanLoadScript());
		ReloadScriptCommand = new RelayCommand(async _ => await ReloadScriptAsync(), _ => CanReloadScript());

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
		autoInjectOnReady = Settings.Default.AutoInjectOnReady;

		// initialize auto side from current position
		ComputeAutoActiveSide();

		ApplyThemeFromSettings();
	}

		public ObservableCollection<SessionModel> Sessions { get; }
		public ObservableCollection<object> Tabs { get; }
		public IInterTabClient InterTabClient { get; }
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
	public ConsoleLogService ConsoleLog => ConsoleLogService.Instance;
	public AccountService AccountService { get; }
	public ICommand OpenAccountManagerCommand { get; }
	public Array FloatingMenuDirectionOptions { get; }

	public bool AutoInjectOnReady
	{
		get => autoInjectOnReady;
		set
		{
			if (autoInjectOnReady == value) return;
			autoInjectOnReady = value;
			Settings.Default.AutoInjectOnReady = value;
			OnPropertyChanged(nameof(AutoInjectOnReady));
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
				SessionCollectionService.Instance.GlobalSelectedSession = value;

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
				HostControl = new ChildClientView()
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
				await sessionManager.InjectAsync(session);
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
				await sessionManager.InjectAsync(session);
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
			var scriptManager = new ScriptManagerView
			{
				Owner = Application.Current.MainWindow
			};
			scriptManager.ShowDialog();
		}

		private void OpenAccountManager()
		{
			var accountManager = new AccountManagerView(AccountService)
			{
				Owner = Application.Current.MainWindow
			};
			accountManager.ShowDialog();
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

		public void TabControl_ClosingItemHandler(ItemActionCallbackArgs<TabablzControl> args)
		{
			if (args.DragablzItem.DataContext is SessionModel session)
			{
				// Only show confirmation if the session is actually running or has been injected
				if (session.State == SessionState.ClientReady || session.InjectionState == InjectionState.Injected)
				{
					var result = MessageBox.Show(
						$"Session '{session.Name}' is active. Are you sure you want to close it?",
						"Close Active Session",
						MessageBoxButton.YesNo,
						MessageBoxImage.Warning);

					if (result != MessageBoxResult.Yes)
					{
						args.Cancel();
						return;
					}
				}

				session.KillProcess();
				Sessions.Remove(session);
				Tabs.Remove(session);
			}
		}

		private bool CanInject() => SelectedSession?.IsInjectable == true;

		private bool CanLoadScript()
		{
			// Use local SelectedSession if available, otherwise fall back to global (for tear-off windows)
			var targetSession = SelectedSession ?? SessionCollectionService.Instance.GlobalSelectedSession;
			return targetSession?.InjectionState == InjectionState.Injected
				&& !string.IsNullOrWhiteSpace(hotReloadScriptPath);
		}

	private bool CanReloadScript() => CanLoadScript();

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
		OpenOrFocusToolTab(
			key: "ScriptControls",
			name: "Script Controls",
			controlFactory: () => new Views.ScriptControlsView(),
			icon: PackIconMaterialKind.ScriptText);
	}

    public void OpenSettingsTab()
    {
        OpenOrFocusToolTab(
            key: "Settings",
            name: "Settings",
            controlFactory: () => new Views.SettingsView(),
            icon: PackIconMaterialKind.Cog);
    }

    public void OpenConsoleTab()
    {
        OpenOrFocusToolTab(
            key: "Console",
            name: "Console",
            controlFactory: () => new Views.ConsoleView(),
            icon: PackIconMaterialKind.Console);
    }

    public void OpenThemeManagerTab()
    {
        OpenOrFocusToolTab(
            key: "ThemeManager",
            name: "Theme Manager",
            controlFactory: () => new Views.ThemeManagerPanel(),
            icon: PackIconMaterialKind.Palette);
    }

    public void OpenSessionsOverviewTab()
    {
        OpenOrFocusToolTab(
            key: "SessionsOverview",
            name: "Sessions",
            controlFactory: () => new Views.SessionsOverviewView(
                new SessionsOverviewViewModel(Sessions, ActivateSession, FocusSession, CloseSession)),
            icon: PackIconMaterialKind.ViewList);
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
			OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
			OnPropertyChanged(nameof(FloatingMenuItemsOrientation));
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

	public Orientation FloatingMenuItemsOrientation => FloatingMenuActiveSide is FloatingMenuDirection.Up or FloatingMenuDirection.Down
		? Orientation.Horizontal
		: Orientation.Vertical;

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
			OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
			OnPropertyChanged(nameof(FloatingMenuItemsOrientation));
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
		}
	}

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
			IsFloatingMenuVisible = false;
			return;
		}

		// Session tabs (RS client windows)
		if (SelectedTab is SessionModel)
		{
			IsFloatingMenuVisible = ShowFloatingMenuOnSessionTabs;
		}
		// Tool tabs (Console, Script Manager, Theme Manager, etc.)
		else
		{
			IsFloatingMenuVisible = ShowFloatingMenuOnToolTabs;
		}
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

	public void ShowFloatingMenu()
	{
		IsFloatingMenuVisible = true;
	}

	public void HideFloatingMenu()
	{
		IsFloatingMenuExpanded = false;
		IsFloatingMenuVisible = false;
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
		OnPropertyChanged(nameof(FloatingMenuPopupPlacement));
		OnPropertyChanged(nameof(FloatingMenuItemsOrientation));
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

			// Use local SelectedSession if available, otherwise fall back to global (for tear-off windows)
			var targetSession = SelectedSession ?? SessionCollectionService.Instance.GlobalSelectedSession;

			ConsoleLog.Append($"[OrbitCmd] Requesting load for '{path}' (session PID {targetSession?.RSProcess?.Id})", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
			// Use RELOAD for both initial and subsequent loads to avoid legacy runtime restart; send to selected session
			var success = targetSession?.RSProcess != null
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

			// Use local SelectedSession if available, otherwise fall back to global (for tear-off windows)
			var targetSession = SelectedSession ?? SessionCollectionService.Instance.GlobalSelectedSession;

			ConsoleLog.Append($"[OrbitCmd] Requesting reload for '{path}' (session PID {targetSession?.RSProcess?.Id})", ConsoleLogSource.Orbit, ConsoleLogLevel.Info);
			var success = targetSession?.RSProcess != null
				? await OrbitCommandClient.SendReloadAsync(path, targetSession.RSProcess.Id, CancellationToken.None)
				: await OrbitCommandClient.SendReloadAsync(path, CancellationToken.None);
			if (!success)
			{
				ConsoleLog.Append("[OrbitCmd] Failed to send reload command.", ConsoleLogSource.Orbit, ConsoleLogLevel.Warning);
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

		private void ActivateSession(SessionModel session)
		{
			if (session == null)
				return;

			SelectedSession = session;
		}

		private void FocusSession(SessionModel session)
		{
			session?.SetFocus();
		}

		private void CloseSession(SessionModel session)
		{
			if (session == null)
				return;

			session.KillProcess();
			Sessions.Remove(session);
		}

		private void OnSessionsCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
		{
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
