using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Application = System.Windows.Application;

namespace Orbit.ViewModels;

/// <summary>
/// Backing view model for the script manager panel. Keeps recents, favorites, and the filesystem library
/// in sync while routing load commands to the currently targeted session.
/// </summary>
public class ScriptManagerViewModel : INotifyPropertyChanged, IDisposable
{
	private readonly ScriptManagerService _scriptService;
	private readonly ScriptOrchestrationService Orchestration;
	private readonly SessionCollectionService _sessionCollectionService;
	private readonly McpBridgeClientService _mcpBridgeClient;
	private readonly ObservableCollection<ScriptProfile> _favorites;
	private readonly ObservableCollection<ScriptProfile> _libraryScripts = new();
	private readonly ObservableCollection<ScriptProfile> _browserScripts = new();
	private readonly ObservableCollection<RuntimeScriptDiagnostic> _runtimeScripts = new();
	private readonly HashSet<string> _libraryPaths = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _recentPaths = new(StringComparer.OrdinalIgnoreCase);
	private ScriptProfile? _selectedScript;
	private string _hotReloadScriptPath = string.Empty;
	private string _selectedScriptId = string.Empty;
	private SessionModel? _targetSession;
	private bool _isScriptCommandInFlight;
	private bool _isDiagnosticsBusy;
	private string _diagnosticsStatus = "Runtime diagnostics are not loaded yet.";
	private ScriptBrowserFilter _selectedFilter = ScriptBrowserFilter.All;
	private ScriptBrowserLayout _selectedLayout = ScriptBrowserLayout.List;
	private bool _isMcpToolEnabled;
	private bool _disposed;
	private CancellationTokenSource? _diagnosticsRequest;

	/// <summary>
	/// Convenience constructor used by XAML design-time. Creates service instances on demand.
	/// </summary>
	public ScriptManagerViewModel()
		: this(ResolveScriptManagerService(), ResolveSessionCollectionService(), ResolveMcpBridgeClientService())
	{
	}

	private static ScriptManagerService ResolveScriptManagerService()
	{
		var app = Application.Current as App;
		var service = app?.Services.GetService<ScriptManagerService>();
		if (service == null)
		{
			throw new InvalidOperationException("ScriptManagerViewModel requires ScriptManagerService from DI.");
		}

		return service;
	}

	private static SessionCollectionService ResolveSessionCollectionService()
	{
		var app = Application.Current as App;
		var service = app?.Services.GetService<SessionCollectionService>();
		if (service == null)
		{
			throw new InvalidOperationException("ScriptManagerViewModel requires SessionCollectionService from DI.");
		}

		return service;
	}

	private static McpBridgeClientService ResolveMcpBridgeClientService()
	{
		var app = Application.Current as App;
		var service = app?.Services.GetService<McpBridgeClientService>();
		if (service == null)
		{
			throw new InvalidOperationException("ScriptManagerViewModel requires McpBridgeClientService from DI.");
		}

		return service;
	}

	/// <summary>
	/// Primary constructor used by the runtime. Accepts the script catalog + shared session collection services.
	/// </summary>
	public ScriptManagerViewModel(
		ScriptManagerService scriptService,
		SessionCollectionService sessionCollectionService,
		McpBridgeClientService mcpBridgeClient)
	{
		_scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));
		_scriptService.PropertyChanged += OnStorageChanged;
		Orchestration = new ScriptOrchestrationService(_scriptService, ConsoleLogService.Instance, new OrbitCommandClientAdapter());
		_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
		_mcpBridgeClient = mcpBridgeClient ?? throw new ArgumentNullException(nameof(mcpBridgeClient));
		_isMcpToolEnabled = Settings.Default.MesharpIntegrationEnabled;
		Settings.Default.PropertyChanged += OnAppSettingsPropertyChanged;

		// Initialize favorites collection
		_favorites = new ObservableCollection<ScriptProfile>(_scriptService.GetFavorites());

		// Subscribe to changes in RecentScripts to update favorites
		_scriptService.Profiles.CollectionChanged += OnRecentScriptsChanged;
		_scriptService.Profiles.CollectionChanged += OnRecentScriptsCollectionChanged;
		foreach (var profile in _scriptService.Profiles)
		{
			profile.PropertyChanged += OnScriptProfilePropertyChanged;
		}

		RecentScriptsView = new ListCollectionView(_scriptService.Profiles);
		if (RecentScriptsView != null)
		{
			RecentScriptsView.Filter = ShouldIncludeInRecents;
		}

		BrowserScriptsView = CollectionViewSource.GetDefaultView(_browserScripts);
		if (BrowserScriptsView != null)
		{
			BrowserScriptsView.Filter = ShouldIncludeInBrowser;
		}

		// Track session changes so tooling stays in sync when sessions open/close
		_sessionCollectionService.PropertyChanged += OnSessionCollectionPropertyChanged;
		_sessionCollectionService.Sessions.CollectionChanged += OnSessionsCollectionChanged;

		AddScriptCommand = new RelayCommand(AddScript);
		RemoveScriptCommand = new RelayCommand<ScriptProfile?>(RemoveScript, profile => profile != null);
		ToggleFavoriteCommand = new RelayCommand<ScriptProfile?>(ToggleFavorite, profile => profile != null);
		LoadScriptCommand = new RelayCommand<ScriptProfile?>(async profile => await LoadScriptAsync(profile, false), CanExecuteScriptCommand);
		ReloadScriptCommand = new RelayCommand<ScriptProfile?>(async profile => await LoadScriptAsync(profile, true), CanExecuteScriptCommand);
		StopScriptCommand = new RelayCommand(async () => await StopScriptAsync(), CanStopScript);
		RefreshDiagnosticsCommand = new AsyncRelayCommand(RefreshRuntimeDiagnosticsAsync, CanRefreshDiagnostics);
		ClearMissingCommand = new RelayCommand(ClearMissing);

		_hotReloadScriptPath = Settings.Default.HotReloadScriptPath ?? string.Empty;
		_selectedScript = _scriptService.Profiles.FirstOrDefault(p =>
			string.Equals(p.FilePath, _hotReloadScriptPath, StringComparison.OrdinalIgnoreCase));
		_selectedScriptId = _selectedScript?.ScriptId ?? string.Empty;

		var initialTarget = _sessionCollectionService.GlobalHotReloadTargetSession
			?? _sessionCollectionService.GlobalSelectedSession
			?? _sessionCollectionService.Sessions.FirstOrDefault();
		TargetSession = initialTarget;

		RefreshLibraryScripts();
		RecentScriptsView?.Refresh();
		UpdateCommandStates();
		_ = RefreshRuntimeDiagnosticsAsync();
	}

	public ICollectionView RecentScriptsView { get; private set; } = null!;
	public ICollectionView BrowserScriptsView { get; private set; } = null!;

	public ObservableCollection<ScriptProfile> LibraryScripts => _libraryScripts;
	public ObservableCollection<ScriptProfile> BrowserScripts => _browserScripts;
	public ObservableCollection<RuntimeScriptDiagnostic> RuntimeScripts => _runtimeScripts;

	public ObservableCollection<ScriptProfile> Favorites => _favorites;
	public string? StorageError => _scriptService.LastError;
	private void OnStorageChanged(object? sender, PropertyChangedEventArgs e) { if (e.PropertyName == nameof(ScriptManagerService.LastError)) OnPropertyChanged(nameof(StorageError)); }
	public bool IsMcpToolEnabled => _isMcpToolEnabled;

	public ObservableCollection<SessionModel> Sessions => _sessionCollectionService.Sessions;

	public ScriptProfile? SelectedScript
	{
		get => _selectedScript;
		set
		{
			if (ReferenceEquals(_selectedScript, value))
				return;

			_selectedScript = value;
			if (value?.FileExists == true)
			{
				HotReloadScriptPath = value.FilePath;
			}

			if (value != null)
			{
				SelectedScriptId = value.ScriptId;
			}

			OnPropertyChanged(nameof(SelectedScript));
			LoadScriptCommand.NotifyCanExecuteChanged();
			ReloadScriptCommand.NotifyCanExecuteChanged();
		}
	}

	public string HotReloadScriptPath
	{
		get => _hotReloadScriptPath;
		set
		{
			var normalized = value ?? string.Empty;
			if (string.Equals(_hotReloadScriptPath, normalized, StringComparison.Ordinal))
				return;

			_hotReloadScriptPath = normalized;
			Settings.Default.HotReloadScriptPath = _hotReloadScriptPath;

			// Try to keep SelectedScript in sync with manual edits
			var match = _scriptService.FindByPath(_hotReloadScriptPath);
			if (!ReferenceEquals(_selectedScript, match))
			{
				_selectedScript = match;
				OnPropertyChanged(nameof(SelectedScript));
			}
			if (match != null && !string.IsNullOrWhiteSpace(match.ScriptId))
			{
				SelectedScriptId = match.ScriptId;
			}

			OnPropertyChanged(nameof(HotReloadScriptPath));
			LoadScriptCommand.NotifyCanExecuteChanged();
			ReloadScriptCommand.NotifyCanExecuteChanged();
		}
	}

	public string SelectedScriptId
	{
		get => _selectedScriptId;
		set
		{
			var normalized = value?.Trim() ?? string.Empty;
			if (string.Equals(_selectedScriptId, normalized, StringComparison.Ordinal))
				return;

			_selectedScriptId = normalized;
			OnPropertyChanged(nameof(SelectedScriptId));
		}
	}

	/// <summary>
	/// Session that script load/reload commands should target. This stays synchronized with the global hot reload target.
	/// </summary>
	public SessionModel? TargetSession
	{
		get => _targetSession;
		set
		{
			if (ReferenceEquals(_targetSession, value))
				return;

			if (_targetSession != null)
			{
				_targetSession.PropertyChanged -= OnTargetSessionPropertyChanged;
			}

			_targetSession = value;
			_diagnosticsRequest?.Cancel();
			_runtimeScripts.Clear();
			OnPropertyChanged(nameof(HasRuntimeScripts));
			OnPropertyChanged(nameof(HasNoRuntimeScripts));
			if (_targetSession != null)
			{
				_targetSession.PropertyChanged += OnTargetSessionPropertyChanged;
			}

			_sessionCollectionService.GlobalHotReloadTargetSession = value;
			OnPropertyChanged(nameof(TargetSession));
			OnPropertyChanged(nameof(TargetSessionScriptStatus));
			OnPropertyChanged(nameof(TargetRuntimeInventory));
			OnPropertyChanged(nameof(TargetSessionActiveScriptName));
			OnPropertyChanged(nameof(TargetSessionHasActiveScript));
			OnPropertyChanged(nameof(TargetSessionLastChangedDisplay));
			UpdateCommandStates();
			_ = RefreshRuntimeDiagnosticsAsync();
		}
	}

	public string TargetSessionScriptStatus => TargetSession?.ScriptRuntimeStatus ?? "No session selected";
	public string TargetRuntimeInventory => TargetSession?.RuntimeInventorySummary ?? "No session selected";
	public string TargetSessionActiveScriptName => TargetSession?.ActiveScriptName ?? "None";
	public bool TargetSessionHasActiveScript => TargetSession?.HasActiveScript == true;
	public string TargetSessionLastChangedDisplay => TargetSession?.ScriptLastChangedAt?.ToString("g") ?? "N/A";

	public ScriptBrowserFilter SelectedFilter
	{
		get => _selectedFilter;
		set
		{
			if (_selectedFilter == value)
				return;

			_selectedFilter = value;
			OnPropertyChanged(nameof(SelectedFilter));
			OnPropertyChanged(nameof(IsAllFilter));
			OnPropertyChanged(nameof(IsLibraryFilter));
			OnPropertyChanged(nameof(IsFavoritesFilter));
			OnPropertyChanged(nameof(IsRecentsFilter));
			OnPropertyChanged(nameof(IsMissingFilter));
			BrowserScriptsView?.Refresh();
		}
	}

	public ScriptBrowserLayout SelectedLayout
	{
		get => _selectedLayout;
		set
		{
			if (_selectedLayout == value)
				return;

			_selectedLayout = value;
			OnPropertyChanged(nameof(SelectedLayout));
			OnPropertyChanged(nameof(IsListLayout));
			OnPropertyChanged(nameof(IsTileLayout));
		}
	}

	public bool IsListLayout => _selectedLayout == ScriptBrowserLayout.List;
	public bool IsTileLayout => _selectedLayout == ScriptBrowserLayout.Tiles;

	public bool IsAllFilter => _selectedFilter == ScriptBrowserFilter.All;
	public bool IsLibraryFilter => _selectedFilter == ScriptBrowserFilter.Library;
	public bool IsFavoritesFilter => _selectedFilter == ScriptBrowserFilter.Favorites;
	public bool IsRecentsFilter => _selectedFilter == ScriptBrowserFilter.Recents;
	public bool IsMissingFilter => _selectedFilter == ScriptBrowserFilter.Missing;

	public bool HasFavorites => _favorites.Count > 0;
	public bool HasSessions => _sessionCollectionService.Sessions.Count > 0;
	public bool HasLibraryScripts => _libraryScripts.Count > 0;
	public bool HasRuntimeScripts => _runtimeScripts.Count > 0;
	public bool HasNoRuntimeScripts => !HasRuntimeScripts;
	public bool IsDiagnosticsBusy
	{
		get => _isDiagnosticsBusy;
		private set
		{
			if (_isDiagnosticsBusy == value)
				return;
			_isDiagnosticsBusy = value;
			OnPropertyChanged(nameof(IsDiagnosticsBusy));
			RefreshDiagnosticsCommand.NotifyCanExecuteChanged();
		}
	}

	public string DiagnosticsStatus
	{
		get => _diagnosticsStatus;
		private set
		{
			if (string.Equals(_diagnosticsStatus, value, StringComparison.Ordinal))
				return;
			_diagnosticsStatus = value;
			OnPropertyChanged(nameof(DiagnosticsStatus));
		}
	}
	public string LibraryDirectoryPath => _scriptService.DefaultLibraryPath;
	public bool ScriptWindowEmbeddingEnabled
	{
		get => Settings.Default.ScriptWindowEmbeddingEnabled;
		set
		{
			if (Settings.Default.ScriptWindowEmbeddingEnabled == value)
			{
				return;
			}

			Settings.Default.ScriptWindowEmbeddingEnabled = value;
			try
			{
				Settings.Default.Save();
			}
			catch (Exception ex)
			{
				ConsoleLogService.Instance.Append(
					$"[OrbitAPI] Failed to persist script window embedding preference: {ex.Message}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
			}

			OnPropertyChanged(nameof(ScriptWindowEmbeddingEnabled));

			ConsoleLogService.Instance.Append(
				value
					? "[OrbitAPI] Script window embedding enabled."
					: "[OrbitAPI] Script window embedding disabled.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Info);
		}
	}

	public IRelayCommand AddScriptCommand { get; }
	public IRelayCommand<ScriptProfile?> RemoveScriptCommand { get; }
	public IRelayCommand<ScriptProfile?> ToggleFavoriteCommand { get; }
	public IRelayCommand<ScriptProfile?> LoadScriptCommand { get; }
	public IRelayCommand<ScriptProfile?> ReloadScriptCommand { get; }
	public IRelayCommand StopScriptCommand { get; }
	public IAsyncRelayCommand RefreshDiagnosticsCommand { get; }
	public IRelayCommand ClearMissingCommand { get; }

	public event PropertyChangedEventHandler? PropertyChanged;

	/// <summary>
	/// Prompts the user for a script DLL and adds/updates its profile in the recents list.
	/// </summary>
	private void AddScript()
	{
		var dialog = new OpenFileDialog
		{
			Filter = "DLL Files (*.dll)|*.dll|All Files (*.*)|*.*",
			Title = "Select Script DLL",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\MemoryError\\CSharp_scripts\\"
		};

		if (dialog.ShowDialog() == true)
		{
			_scriptService.AddOrUpdateScript(dialog.FileName);
			RefreshFavorites();
			RefreshLibraryScripts();
			RecentScriptsView?.Refresh();
		}
	}

	/// <summary>
	/// Removes a script from recents (or hides it when pinned as favorite) after user confirmation.
	/// </summary>
	private void RemoveScript(ScriptProfile? profile)
	{
		if (profile == null) return;

		var tracked = EnsureTrackedProfile(profile);

		var result = MessageBox.Show(
			$"Remove '{profile.Name}' from recents?",
			"Confirm Removal",
			MessageBoxButton.YesNo,
			MessageBoxImage.Question);

		if (result == MessageBoxResult.Yes)
		{
			if (tracked != null) _scriptService.RemoveScript(tracked);

			RefreshFavorites();
			RecentScriptsView?.Refresh();
			RefreshLibraryScripts();
		}
	}

	/// <summary>
	/// Flips the favorite flag on the provided script, ensuring it remains visible in recents as needed.
	/// </summary>
	private void ToggleFavorite(ScriptProfile? profile)
	{
		if (profile == null) return;

		var tracked = EnsureTrackedProfile(profile);
		if (tracked == null)
			return;

		if (tracked.HideFromRecents && !tracked.IsFavorite)
		{
			tracked.HideFromRecents = false;
		}

		_scriptService.ToggleFavorite(tracked);
		RefreshFavorites();
		RecentScriptsView?.Refresh();
	}

	/// <summary>
	/// Sends a hot-reload request for the given script to the targeted session, validating injection state first.
	/// </summary>
	private async Task LoadScriptAsync(ScriptProfile? profile, bool isReload)
    {
		if (_isScriptCommandInFlight)
		{
			return;
		}

		if (!Settings.Default.MesharpIntegrationEnabled)
		{
			ConsoleLogService.Instance.Append(
				"[ScriptManager] MESharp integration is disabled in Settings -> Advanced. Script load/reload is unavailable.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		var resolvedProfile = ResolveExecutableProfile(profile);
		if (resolvedProfile == null)
		{
			ConsoleLogService.Instance.Append(
				"[ScriptManager] Select a script or enter a valid path before loading.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		var tracked = EnsureTrackedProfile(resolvedProfile);
		if (tracked == null)
			return;

		// Update last used
		tracked.LastUsed = DateTime.Now;
		var resolvedScriptId = ResolveScriptId(tracked);
		_scriptService.AddOrUpdateScript(tracked.FilePath, tracked.Name, tracked.Description, resolvedScriptId);
		tracked.ScriptId = resolvedScriptId;
		SelectedScriptId = resolvedScriptId;
		tracked.HideFromRecents = false;
		RecentScriptsView?.Refresh();

		// Resolve the hot reload target, preferring the shared target but falling back to the global selection.
		var targetSession = ResolveOrSelectTargetSession();

		if (targetSession == null)
		{
			ConsoleLogService.Instance.Append(
				"[ScriptManager] No session available. Start or select a session before loading scripts.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		if (targetSession.InjectionState != InjectionState.Injected)
		{
			ConsoleLogService.Instance.Append(
				$"[ScriptManager] Session '{targetSession.Name}' is not injected. Inject MESharp before loading scripts.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		if (targetSession.RSProcess == null)
		{
			ConsoleLogService.Instance.Append(
				$"[ScriptManager] Session '{targetSession.Name}' has no active process. Reload the session before loading scripts.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		var pid = targetSession.RSProcess.Id;

		var verb = isReload ? "reload" : "load";
		targetSession.SetScriptRuntimePending(isReload ? "Reloading script" : "Loading script");
		ConsoleLogService.Instance.Append(
			$"[ScriptManager] Requesting {verb} for '{tracked.Name}' as scriptId '{resolvedScriptId}' to session '{targetSession.Name}' (PID {pid})",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		try
		{
			_isScriptCommandInFlight = true;
			UpdateCommandStates();

			var success = isReload
				? await Orchestration.ReloadAsync(targetSession, tracked.FilePath, resolvedScriptId)
				: await Orchestration.LoadAsync(targetSession, tracked.FilePath, resolvedScriptId);
			if (!success) return;
		}
		finally
		{
			_isScriptCommandInFlight = false;
			UpdateCommandStates();
		}

		await RefreshRuntimeDiagnosticsAsync().ConfigureAwait(true);

	}

	private bool CanExecuteScriptCommand(ScriptProfile? profile)
	{
		if (!Settings.Default.MesharpIntegrationEnabled)
		{
			return false;
		}

		if (_isScriptCommandInFlight)
		{
			return false;
		}
		if (_disposed || !IsUsableScriptTarget(TargetSession) || !_sessionCollectionService.Sessions.Contains(TargetSession!))
			return false;

		if (profile != null) return profile.FileExists;

		return !string.IsNullOrWhiteSpace(HotReloadScriptPath) && File.Exists(HotReloadScriptPath);
	}

	private bool CanStopScript()
	{
		if (!Settings.Default.MesharpIntegrationEnabled)
		{
			return false;
		}

		if (_isScriptCommandInFlight)
		{
			return false;
		}

		var target = TargetSession;
		return !_disposed && IsUsableScriptTarget(target) && _sessionCollectionService.Sessions.Contains(target!);
	}

	private bool CanRefreshDiagnostics()
	{
		if (!_isMcpToolEnabled)
		{
			return false;
		}

		if (IsDiagnosticsBusy)
		{
			return false;
		}

		var target = TargetSession;
		return !_disposed && IsUsableScriptTarget(target) && _sessionCollectionService.Sessions.Contains(target!);
	}

	private string ResolveScriptId(ScriptProfile profile)
	{
		if (!string.IsNullOrWhiteSpace(SelectedScriptId) &&
			string.Equals(profile.FilePath, HotReloadScriptPath, StringComparison.OrdinalIgnoreCase))
		{
			return SelectedScriptId.Trim();
		}

		if (!string.IsNullOrWhiteSpace(profile.ScriptId))
		{
			return profile.ScriptId.Trim();
		}

		return ScriptManagerService.DeriveScriptIdFromPath(profile.FilePath);
	}

	private string? ResolveScriptIdForStop(SessionModel targetSession)
	{
		if (!string.IsNullOrWhiteSpace(SelectedScriptId))
		{
			return SelectedScriptId.Trim();
		}

		if (!string.IsNullOrWhiteSpace(targetSession.ActiveScriptId))
		{
			return targetSession.ActiveScriptId;
		}

		return null;
	}

	private async Task StopScriptAsync()
	{
		if (_isScriptCommandInFlight)
		{
			return;
		}

		if (!Settings.Default.MesharpIntegrationEnabled)
		{
			ConsoleLogService.Instance.Append(
				"[ScriptManager] MESharp integration is disabled in Settings -> Advanced. Script stop is unavailable.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		var targetSession = ResolveOrSelectTargetSession();
		if (targetSession == null)
		{
			ConsoleLogService.Instance.Append(
				"[ScriptManager] No session available. Start or select a session before stopping scripts.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		if (targetSession.InjectionState != InjectionState.Injected || targetSession.RSProcess == null)
		{
			targetSession.SetScriptRuntimeError("Session is not injected.");
			ConsoleLogService.Instance.Append(
				$"[ScriptManager] Session '{targetSession.Name}' is not injected. Inject MESharp before stopping scripts.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return;
		}

		targetSession.SetScriptRuntimePending("Stopping script");
		try
		{
			_isScriptCommandInFlight = true;
			UpdateCommandStates();

			var scriptIdToUnload = ResolveScriptIdForStop(targetSession);
			var success = string.IsNullOrWhiteSpace(scriptIdToUnload)
				? await Orchestration.UnloadScriptAsync(targetSession, "default")
				: await Orchestration.UnloadScriptAsync(targetSession, scriptIdToUnload);
			if (!success) return;
			ConsoleLogService.Instance.Append(
				string.IsNullOrWhiteSpace(scriptIdToUnload)
					? $"[ScriptManager] Stopped script for session '{targetSession.Name}'."
					: $"[ScriptManager] Stopped script '{scriptIdToUnload}' for session '{targetSession.Name}'.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Info);
		}
		finally
		{
			_isScriptCommandInFlight = false;
			UpdateCommandStates();
		}

		await RefreshRuntimeDiagnosticsAsync().ConfigureAwait(true);
	}

	private ScriptProfile? ResolveExecutableProfile(ScriptProfile? profile)
	{
		if (profile != null) return profile.FileExists ? profile : null;

		if (!string.IsNullOrWhiteSpace(HotReloadScriptPath) && File.Exists(HotReloadScriptPath))
		{
			var existing = _scriptService.FindByPath(HotReloadScriptPath);
			if (existing != null)
			{
				return existing;
			}

			return new ScriptProfile
			{
				FilePath = HotReloadScriptPath,
				Name = Path.GetFileNameWithoutExtension(HotReloadScriptPath),
				Description = string.Empty
			};
		}

		return null;
	}

	private SessionModel? ResolveOrSelectTargetSession()
	{
		return !_disposed && IsUsableScriptTarget(TargetSession) && _sessionCollectionService.Sessions.Contains(TargetSession!)
			? TargetSession : null;
	}

	private static bool IsUsableScriptTarget(SessionModel? session)
	{
		if (session == null || !session.IsRuneScapeClient || session.InjectionState != InjectionState.Injected
			|| session.State is SessionState.Closed or SessionState.ShuttingDown || session.RSProcess == null) return false;
		try { return !session.RSProcess.HasExited; }
		catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
	}

	private async Task RefreshRuntimeDiagnosticsAsync()
	{
		if (_disposed) return;
		_diagnosticsRequest?.Cancel();
		using var request = new CancellationTokenSource();
		_diagnosticsRequest = request;
		bool IsCurrent() => !_disposed && !request.IsCancellationRequested && ReferenceEquals(_diagnosticsRequest, request);
		if (!_isMcpToolEnabled)
		{
			_runtimeScripts.Clear();
			OnPropertyChanged(nameof(HasRuntimeScripts));
			OnPropertyChanged(nameof(HasNoRuntimeScripts));
			DiagnosticsStatus = "MESharp integration is disabled.";
			_diagnosticsRequest = null;
			IsDiagnosticsBusy = false;
			return;
		}

		var targetSession = ResolveOrSelectTargetSession();
		if (!IsUsableScriptTarget(targetSession))
		{
			_runtimeScripts.Clear();
			OnPropertyChanged(nameof(HasRuntimeScripts));
			OnPropertyChanged(nameof(HasNoRuntimeScripts));
			DiagnosticsStatus = "Select an injected session to read runtime diagnostics.";
			_diagnosticsRequest = null;
			IsDiagnosticsBusy = false;
			return;
		}

		var pid = targetSession.RSProcess.Id;
		IsDiagnosticsBusy = true;
		DiagnosticsStatus = $"Refreshing runtime diagnostics for PID {pid}...";
		try
		{
			var listResult = await _mcpBridgeClient
				.CallAsync(pid, "script.list", new { }, request.Token)
				.ConfigureAwait(true);
			if (!IsCurrent() || !ReferenceEquals(TargetSession, targetSession)) return;

			if (!listResult.IsSuccess || string.IsNullOrWhiteSpace(listResult.PayloadJson))
			{
				_runtimeScripts.Clear();
				OnPropertyChanged(nameof(HasRuntimeScripts));
				OnPropertyChanged(nameof(HasNoRuntimeScripts));
				DiagnosticsStatus = $"script.list failed: {listResult.Message}";
				return;
			}

			var scripts = ParseScriptList(listResult.PayloadJson);
			var diagnostics = new List<RuntimeScriptDiagnostic>(scripts.Count);

			foreach (var script in scripts)
			{
				var stateResult = await _mcpBridgeClient
					.CallAsync(pid, "script.get_state", new { scriptId = script.ScriptId }, request.Token)
					.ConfigureAwait(true);
				if (!IsCurrent() || !ReferenceEquals(TargetSession, targetSession)) return;

				if (stateResult.IsSuccess && !string.IsNullOrWhiteSpace(stateResult.PayloadJson))
				{
					var merged = MergeWithStatePayload(script, stateResult.PayloadJson);
					diagnostics.Add(merged);
				}
				else
				{
					diagnostics.Add(script with
					{
						LastError = string.IsNullOrWhiteSpace(script.LastError)
							? stateResult.Message
							: $"{script.LastError}; state failed: {stateResult.Message}"
					});
				}
			}

			_runtimeScripts.Clear();
			foreach (var item in diagnostics.OrderByDescending(d => d.IsLoaded).ThenBy(d => d.ScriptId, StringComparer.OrdinalIgnoreCase))
			{
				_runtimeScripts.Add(item);
			}

			OnPropertyChanged(nameof(HasRuntimeScripts));
			OnPropertyChanged(nameof(HasNoRuntimeScripts));
			DiagnosticsStatus = _runtimeScripts.Count == 0
				? $"No loaded scripts reported for PID {pid}."
				: $"Runtime reports {_runtimeScripts.Count} script(s) for PID {pid}.";
		}
		catch (Exception ex)
		{
			if (!IsCurrent()) return;
			_runtimeScripts.Clear();
			OnPropertyChanged(nameof(HasRuntimeScripts));
			OnPropertyChanged(nameof(HasNoRuntimeScripts));
			DiagnosticsStatus = $"Runtime diagnostics failed: {ex.Message}";
		}
		finally
		{
			if (ReferenceEquals(_diagnosticsRequest, request))
			{
				_diagnosticsRequest = null;
				IsDiagnosticsBusy = false;
			}
		}
	}

	private static List<RuntimeScriptDiagnostic> ParseScriptList(string payloadJson)
	{
		var scripts = new List<RuntimeScriptDiagnostic>();
		using var doc = JsonDocument.Parse(payloadJson);
		if (!doc.RootElement.TryGetProperty("scripts", out var scriptsEl) || scriptsEl.ValueKind != JsonValueKind.Array)
		{
			return scripts;
		}

		foreach (var item in scriptsEl.EnumerateArray())
		{
			var scriptId = ReadString(item, "scriptId") ?? "default";
			var path = ReadString(item, "path");
			var isLoaded = ReadBool(item, "isLoaded");
			var info = ReadString(item, "info");
			scripts.Add(new RuntimeScriptDiagnostic
			{
				ScriptId = scriptId,
				Path = path,
				IsLoaded = isLoaded ?? false,
				Info = info ?? string.Empty
			});
		}

		return scripts;
	}

	private static RuntimeScriptDiagnostic MergeWithStatePayload(RuntimeScriptDiagnostic baseItem, string statePayloadJson)
	{
		using var doc = JsonDocument.Parse(statePayloadJson);
		if (!doc.RootElement.TryGetProperty("scriptState", out var stateEl) || stateEl.ValueKind != JsonValueKind.Object)
		{
			return baseItem;
		}

		return baseItem with
		{
			ScriptId = ReadString(stateEl, "scriptId") ?? baseItem.ScriptId,
			IsLoaded = ReadBool(stateEl, "isLoaded") ?? baseItem.IsLoaded,
			AssemblyName = ReadString(stateEl, "assemblyName"),
			AssemblyVersion = ReadString(stateEl, "assemblyVersion"),
			LifecycleState = ReadString(stateEl, "scriptLifecycleState"),
			Path = ReadString(stateEl, "lastLoadedPath") ?? baseItem.Path,
			LoadedAtUtc = ReadDateTime(stateEl, "loadedAtUtc"),
			LastStatusUtc = ReadDateTime(stateEl, "lastStatusUtc"),
			LastError = ReadString(stateEl, "lastError"),
			ContextAlive = ReadBool(stateEl, "contextAlive"),
			MainScriptType = ReadString(stateEl, "mainScriptType"),
			MainScriptState = ReadString(stateEl, "mainScriptState"),
			MainScriptStatus = ReadString(stateEl, "mainScriptStatus"),
			MainScriptHasUserSelectedTarget = ReadBool(stateEl, "mainScriptHasUserSelectedTarget"),
			MainScriptActiveCombatTarget = ReadString(stateEl, "mainScriptActiveCombatTarget")
		};
	}

	private static string? ReadString(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value))
		{
			return null;
		}

		return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
	}

	private static bool? ReadBool(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			_ => null
		};
	}

	private static DateTime? ReadDateTime(JsonElement element, string name)
	{
		if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		return value.TryGetDateTime(out var parsed) ? parsed : null;
	}

	private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		OnPropertyChanged(nameof(HasSessions));

		if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
		{
			if (_targetSession != null && !_sessionCollectionService.Sessions.Contains(_targetSession))
			{
				TargetSession = null;
			}
		}

		UpdateCommandStates();
		_ = RefreshRuntimeDiagnosticsAsync();
	}

	private void OnSessionCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SessionCollectionService.GlobalHotReloadTargetSession))
		{
			var sharedTarget = _sessionCollectionService.GlobalHotReloadTargetSession;
			if (!ReferenceEquals(_targetSession, sharedTarget))
			{
				TargetSession = sharedTarget;
			}
			_ = RefreshRuntimeDiagnosticsAsync();
		}
	}

	private void OnAppSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (!string.Equals(e.PropertyName, nameof(Settings.Default.MesharpIntegrationEnabled), StringComparison.Ordinal))
		{
			return;
		}

		var enabled = Settings.Default.MesharpIntegrationEnabled;
		if (_isMcpToolEnabled == enabled)
		{
			return;
		}

		_isMcpToolEnabled = enabled;
		OnPropertyChanged(nameof(IsMcpToolEnabled));
		UpdateCommandStates();
		_ = RefreshRuntimeDiagnosticsAsync();
	}

	private void OnTargetSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(SessionModel.InjectionState) or nameof(SessionModel.RSProcess) or nameof(SessionModel.State))
			_ = RefreshRuntimeDiagnosticsAsync();
		if (e.PropertyName == nameof(SessionModel.RuntimeInventorySummary)) OnPropertyChanged(nameof(TargetRuntimeInventory));
		if (e.PropertyName == nameof(SessionModel.ScriptRuntimeStatus) ||
			e.PropertyName == nameof(SessionModel.ActiveScriptPath) ||
			e.PropertyName == nameof(SessionModel.ActiveScriptId) ||
			e.PropertyName == nameof(SessionModel.ScriptLastChangedAt) ||
			e.PropertyName == nameof(SessionModel.InjectionState) ||
			e.PropertyName == nameof(SessionModel.RSProcess))
		{
			OnPropertyChanged(nameof(TargetSessionScriptStatus));
			OnPropertyChanged(nameof(TargetSessionActiveScriptName));
			OnPropertyChanged(nameof(TargetSessionHasActiveScript));
			OnPropertyChanged(nameof(TargetSessionLastChangedDisplay));
			UpdateCommandStates();
		}
	}

	/// <summary>
	/// Purges recents that point at missing files then refreshes derived collections.
	/// </summary>
	private void ClearMissing()
	{
		_scriptService.ClearNonExisting();
		RefreshFavorites();
		RefreshLibraryScripts();
		RecentScriptsView?.Refresh();
	}

	/// <summary>
	/// Synchronizes the Favorites observable collection with the persisted profiles.
	/// </summary>
	private void RefreshFavorites()
	{
		// Update the favorites collection to match current state
		var currentFavorites = _scriptService.GetFavorites().ToList();

		// Remove favorites that are no longer in the service
		for (int i = _favorites.Count - 1; i >= 0; i--)
		{
			if (!currentFavorites.Any(f => f.FilePath == _favorites[i].FilePath))
			{
				_favorites.RemoveAt(i);
			}
		}

		// Add new favorites that aren't in the collection yet
		foreach (var favorite in currentFavorites)
		{
			if (!_favorites.Any(f => f.FilePath == favorite.FilePath))
			{
				_favorites.Add(favorite);
			}
		}

		OnPropertyChanged(nameof(HasFavorites));
		RefreshBrowserScripts();
	}

	private bool ShouldIncludeInRecents(object obj)
	{
		if (obj is not ScriptProfile profile)
			return false;

		return !profile.HideFromRecents;
	}

	/// <summary>
	/// Rebuilds the library feed from the default scripts directory while avoiding duplicates with recents.
	/// </summary>
	private void RefreshLibraryScripts()
	{
		_libraryScripts.Clear();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var script in _scriptService.EnumerateDefaultScripts().OrderBy(p => p.Name))
		{
			if (seen.Add(script.FilePath))
			{
				_libraryScripts.Add(script);
			}
		}

		OnPropertyChanged(nameof(HasLibraryScripts));
		RefreshBrowserScripts();
	}

	private void OnRecentScriptsChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		RefreshFavorites();
	}

	private void OnRecentScriptsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		if (e.OldItems != null)
		{
			foreach (ScriptProfile item in e.OldItems)
			{
				item.PropertyChanged -= OnScriptProfilePropertyChanged;
			}
		}

		if (e.NewItems != null)
		{
			foreach (ScriptProfile item in e.NewItems)
			{
				item.PropertyChanged += OnScriptProfilePropertyChanged;
			}
		}

		RecentScriptsView?.Refresh();
		RefreshBrowserScripts();
	}

	/// <summary>
	/// Ensures we operate on the canonical instance of a script profile tracked by the service.
	/// </summary>
	private ScriptProfile? EnsureTrackedProfile(ScriptProfile profile)
	{
		if (string.IsNullOrWhiteSpace(profile.FilePath))
			return null;

		var tracked = _scriptService.FindByPath(profile.FilePath);
		if (tracked != null)
			return tracked;

		if (!profile.FileExists)
			return null;

		var scriptId = ResolveScriptId(profile);
		_scriptService.AddOrUpdateScript(profile.FilePath, profile.Name, profile.Description, scriptId);
		tracked = _scriptService.FindByPath(profile.FilePath);
		if (tracked != null)
		{
			tracked.HideFromRecents = false;
			if (!string.IsNullOrWhiteSpace(scriptId))
			{
				tracked.ScriptId = scriptId;
			}
			RecentScriptsView?.Refresh();
		}

		return tracked;
	}

	private void OnScriptProfilePropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(ScriptProfile.HideFromRecents))
		{
			RecentScriptsView?.Refresh();
			RefreshBrowserScripts();
		}
	}

	private void RefreshBrowserScripts()
	{
		_libraryPaths.Clear();
		_libraryPaths.UnionWith(_libraryScripts.Select(script => script.FilePath));
		_recentPaths.Clear();
		_recentPaths.UnionWith(_scriptService.Profiles.Where(script => !script.HideFromRecents).Select(script => script.FilePath));
		var merged = new Dictionary<string, ScriptProfile>(StringComparer.OrdinalIgnoreCase);

		void addRange(IEnumerable<ScriptProfile> source)
		{
			foreach (var script in source)
			{
				if (script == null || string.IsNullOrWhiteSpace(script.FilePath))
					continue;

				if (!merged.ContainsKey(script.FilePath))
				{
					merged.Add(script.FilePath, script);
				}
			}
		}

		addRange(_scriptService.Profiles);
		addRange(_libraryScripts);
		addRange(_favorites);

			var ordered = merged.Values
				.OrderByDescending(s => s.LastUsed)
				.ThenBy(s => s.Name)
				.ToList();

		_browserScripts.Clear();
		foreach (var script in ordered)
		{
			_browserScripts.Add(script);
		}

		BrowserScriptsView?.Refresh();
	}

	private bool ShouldIncludeInBrowser(object obj)
	{
		if (obj is not ScriptProfile script)
			return false;

		return _selectedFilter switch
		{
			ScriptBrowserFilter.Library => _libraryPaths.Contains(script.FilePath),
			ScriptBrowserFilter.Favorites => script.IsFavorite,
			ScriptBrowserFilter.Recents => _recentPaths.Contains(script.FilePath),
			ScriptBrowserFilter.Missing => !script.FileExists,
			_ => true
		};
	}

	private void UpdateCommandStates()
	{
		LoadScriptCommand.NotifyCanExecuteChanged();
		ReloadScriptCommand.NotifyCanExecuteChanged();
		StopScriptCommand.NotifyCanExecuteChanged();
		RefreshDiagnosticsCommand.NotifyCanExecuteChanged();
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_diagnosticsRequest?.Cancel();
		if (RecentScriptsView is CollectionView recentView) recentView.DetachFromSourceCollection();
		if (BrowserScriptsView is CollectionView browserView) browserView.DetachFromSourceCollection();
		Settings.Default.PropertyChanged -= OnAppSettingsPropertyChanged;
		_sessionCollectionService.PropertyChanged -= OnSessionCollectionPropertyChanged;
		_sessionCollectionService.Sessions.CollectionChanged -= OnSessionsCollectionChanged;
		if (_targetSession != null)
		{
			_targetSession.PropertyChanged -= OnTargetSessionPropertyChanged;
		}
		_scriptService.PropertyChanged -= OnStorageChanged;
		_scriptService.Profiles.CollectionChanged -= OnRecentScriptsChanged;
		_scriptService.Profiles.CollectionChanged -= OnRecentScriptsCollectionChanged;
		foreach (var profile in _scriptService.Profiles)
		{
			profile.PropertyChanged -= OnScriptProfilePropertyChanged;
		}
	}
}

public sealed record RuntimeScriptDiagnostic
{
	public string ScriptId { get; init; } = "default";
	public bool IsLoaded { get; init; }
	public string? Path { get; init; }
	public string? Info { get; init; }
	public string? AssemblyName { get; init; }
	public string? AssemblyVersion { get; init; }
	public string? LifecycleState { get; init; }
	public DateTime? LoadedAtUtc { get; init; }
	public DateTime? LastStatusUtc { get; init; }
	public string? LastError { get; init; }
	public bool? ContextAlive { get; init; }
	public string? MainScriptType { get; init; }
	public string? MainScriptState { get; init; }
	public string? MainScriptStatus { get; init; }
	public bool? MainScriptHasUserSelectedTarget { get; init; }
	public string? MainScriptActiveCombatTarget { get; init; }
}
