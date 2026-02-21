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
	private readonly SessionCollectionService _sessionCollectionService;
	private readonly ObservableCollection<ScriptProfile> _favorites;
	private readonly ObservableCollection<ScriptProfile> _libraryScripts = new();
	private readonly ObservableCollection<ScriptProfile> _browserScripts = new();
	private ScriptProfile? _selectedScript;
	private string _hotReloadScriptPath = string.Empty;
	private SessionModel? _targetSession;
	private bool _isScriptCommandInFlight;
	private ScriptBrowserFilter _selectedFilter = ScriptBrowserFilter.All;
	private ScriptBrowserLayout _selectedLayout = ScriptBrowserLayout.List;

	/// <summary>
	/// Convenience constructor used by XAML design-time. Creates service instances on demand.
	/// </summary>
	public ScriptManagerViewModel()
		: this(ResolveScriptManagerService(), ResolveSessionCollectionService())
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

	/// <summary>
	/// Primary constructor used by the runtime. Accepts the script catalog + shared session collection services.
	/// </summary>
	public ScriptManagerViewModel(
		ScriptManagerService scriptService,
		SessionCollectionService sessionCollectionService)
	{
		_scriptService = scriptService ?? throw new ArgumentNullException(nameof(scriptService));
		_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));

		// Initialize favorites collection
		_favorites = new ObservableCollection<ScriptProfile>(_scriptService.GetFavorites());

		// Subscribe to changes in RecentScripts to update favorites
		_scriptService.RecentScripts.CollectionChanged += OnRecentScriptsChanged;
		_scriptService.RecentScripts.CollectionChanged += OnRecentScriptsCollectionChanged;
		foreach (var profile in _scriptService.RecentScripts)
		{
			profile.PropertyChanged += OnScriptProfilePropertyChanged;
		}

		RecentScriptsView = CollectionViewSource.GetDefaultView(_scriptService.RecentScripts);
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
		ClearMissingCommand = new RelayCommand(ClearMissing);

		_hotReloadScriptPath = Settings.Default.HotReloadScriptPath ?? string.Empty;
		_selectedScript = _scriptService.RecentScripts.FirstOrDefault(p =>
			string.Equals(p.FilePath, _hotReloadScriptPath, StringComparison.OrdinalIgnoreCase));

		var initialTarget = _sessionCollectionService.GlobalHotReloadTargetSession
			?? _sessionCollectionService.GlobalSelectedSession
			?? _sessionCollectionService.Sessions.FirstOrDefault();
		TargetSession = initialTarget;

		RefreshFavorites();
		RefreshLibraryScripts();
		RefreshBrowserScripts();
		RecentScriptsView?.Refresh();
		UpdateCommandStates();
	}

	public ICollectionView RecentScriptsView { get; private set; } = null!;
	public ICollectionView BrowserScriptsView { get; private set; } = null!;

	public ObservableCollection<ScriptProfile> LibraryScripts => _libraryScripts;
	public ObservableCollection<ScriptProfile> BrowserScripts => _browserScripts;

	public ObservableCollection<ScriptProfile> Favorites => _favorites;

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

			OnPropertyChanged(nameof(HotReloadScriptPath));
			LoadScriptCommand.NotifyCanExecuteChanged();
			ReloadScriptCommand.NotifyCanExecuteChanged();
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
			if (_targetSession != null)
			{
				_targetSession.PropertyChanged += OnTargetSessionPropertyChanged;
			}

			_sessionCollectionService.GlobalHotReloadTargetSession = value;
			OnPropertyChanged(nameof(TargetSession));
			OnPropertyChanged(nameof(TargetSessionScriptStatus));
			OnPropertyChanged(nameof(TargetSessionActiveScriptName));
			OnPropertyChanged(nameof(TargetSessionHasActiveScript));
			OnPropertyChanged(nameof(TargetSessionLastChangedDisplay));
			UpdateCommandStates();
		}
	}

	public string TargetSessionScriptStatus => TargetSession?.ScriptRuntimeStatus ?? "No session selected";
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
			if (tracked != null && tracked.IsFavorite)
			{
				tracked.HideFromRecents = true;
			}
			else if (tracked != null)
			{
				tracked.PropertyChanged -= OnScriptProfilePropertyChanged;
				_scriptService.RemoveScript(tracked);
			}

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
		_scriptService.AddOrUpdateScript(tracked.FilePath, tracked.Name, tracked.Description);
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

		// WPF-backed scripts can need a clean unload boundary before loading a different assembly.
		// If another script is still marked active, request unload first to avoid overlap/race failures.
		var activePath = targetSession.ActiveScriptPath;
		var switchingScripts = !string.IsNullOrWhiteSpace(activePath) &&
			!string.Equals(activePath, tracked.FilePath, StringComparison.OrdinalIgnoreCase);
		if (switchingScripts)
		{
			var unloaded = await TryUnloadBeforeLoadAsync(targetSession).ConfigureAwait(true);
			if (!unloaded)
			{
				targetSession.SetScriptRuntimeError("Failed to unload previous script before loading a new one.");
				return;
			}
		}

		var pid = targetSession.RSProcess?.Id;

		var verb = isReload ? "reload" : "load";
		targetSession.SetScriptRuntimePending(isReload ? "Reloading script" : "Loading script");
		ConsoleLogService.Instance.Append(
			$"[ScriptManager] Requesting {verb} for '{tracked.Name}' to session '{targetSession.Name}' (PID {pid})",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		try
		{
			_isScriptCommandInFlight = true;
			UpdateCommandStates();

			var runtimeReady = await OrbitCommandClient
				.SendStartRuntimeWithRetryAsync(pid, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
				.ConfigureAwait(true);
			if (!runtimeReady)
			{
				ConsoleLogService.Instance.Append(
					$"[ScriptManager] Unable to start ME .NET runtime for session '{targetSession.Name}'. Load may fail.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
			}

			var success = pid.HasValue
				? await OrbitCommandClient.SendReloadWithRetryAsync(tracked.FilePath, pid.Value, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None)
				: await OrbitCommandClient.SendReloadWithRetryAsync(tracked.FilePath, null, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(200), cancellationToken: CancellationToken.None);

			if (!success)
			{
				targetSession.SetScriptRuntimeError($"Failed to {verb} '{tracked.Name}'.");
				ConsoleLogService.Instance.Append(
					$"[ScriptManager] Failed to send {verb} command for '{tracked.Name}'. Check if MESharp is running.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			targetSession.SetScriptLoaded(tracked.FilePath);
		}
		finally
		{
			_isScriptCommandInFlight = false;
			UpdateCommandStates();
		}
		// Note: success=true only means the command was sent, not that the script loaded.
		// Check the MESharp console for actual script loading results.
	}

	private bool CanExecuteScriptCommand(ScriptProfile? profile)
	{
		if (_isScriptCommandInFlight)
		{
			return false;
		}

		if (profile?.FileExists == true)
		{
			return true;
		}

		return !string.IsNullOrWhiteSpace(HotReloadScriptPath) && File.Exists(HotReloadScriptPath);
	}

	private bool CanStopScript()
	{
		if (_isScriptCommandInFlight)
		{
			return false;
		}

		var target = TargetSession;
		return target != null &&
			target.InjectionState == InjectionState.Injected &&
			target.RSProcess != null;
	}

	private async Task StopScriptAsync()
	{
		if (_isScriptCommandInFlight)
		{
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

			var success = await OrbitCommandClient
				.SendUnloadScriptWithRetryAsync(targetSession.RSProcess.Id, maxAttempts: 3, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
				.ConfigureAwait(true);

			if (!success)
			{
				targetSession.SetScriptRuntimeError("Failed to stop script.");
				ConsoleLogService.Instance.Append(
					$"[ScriptManager] Failed to stop script for session '{targetSession.Name}'.",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Warning);
				return;
			}

			targetSession.SetScriptStopped();
			ConsoleLogService.Instance.Append(
				$"[ScriptManager] Stopped script for session '{targetSession.Name}'.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Info);
		}
		finally
		{
			_isScriptCommandInFlight = false;
			UpdateCommandStates();
		}
	}

	private ScriptProfile? ResolveExecutableProfile(ScriptProfile? profile)
	{
		if (profile?.FileExists == true)
		{
			return profile;
		}

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
		var targetSession = TargetSession;
		if (IsUsableScriptTarget(targetSession))
		{
			return targetSession;
		}

		var fallback = GetPreferredScriptTarget();
		if (fallback != null)
		{
			TargetSession = fallback;
			targetSession = fallback;
		}

		return targetSession;
	}

	private SessionModel? GetPreferredScriptTarget()
	{
		var preferredCandidates = new[]
		{
			_sessionCollectionService.GlobalHotReloadTargetSession,
			_sessionCollectionService.GlobalSelectedSession
		};

		foreach (var candidate in preferredCandidates)
		{
			if (IsUsableScriptTarget(candidate))
			{
				return candidate;
			}
		}

		return _sessionCollectionService.Sessions.FirstOrDefault(IsUsableScriptTarget);
	}

	private static bool IsUsableScriptTarget(SessionModel? session)
	{
		return session != null &&
			session.IsRuneScapeClient &&
			session.InjectionState == InjectionState.Injected &&
			session.RSProcess != null;
	}

	private async Task<bool> TryUnloadBeforeLoadAsync(SessionModel targetSession)
	{
		if (targetSession.RSProcess == null)
		{
			return false;
		}

		ConsoleLogService.Instance.Append(
			$"[ScriptManager] Unloading currently active script in session '{targetSession.Name}' before loading a new script.",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		var unloaded = await OrbitCommandClient
			.SendUnloadScriptWithRetryAsync(targetSession.RSProcess.Id, maxAttempts: 4, initialDelay: TimeSpan.FromMilliseconds(150), cancellationToken: CancellationToken.None)
			.ConfigureAwait(true);

		if (!unloaded)
		{
			ConsoleLogService.Instance.Append(
				$"[ScriptManager] Failed to unload previous script for session '{targetSession.Name}'.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
			return false;
		}

		targetSession.SetScriptStopped();
		await Task.Delay(250).ConfigureAwait(true);
		return true;
	}

	private void OnSessionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
	{
		OnPropertyChanged(nameof(HasSessions));

		if (e.Action == NotifyCollectionChangedAction.Remove || e.Action == NotifyCollectionChangedAction.Reset)
		{
			if (_targetSession != null && !_sessionCollectionService.Sessions.Contains(_targetSession))
			{
				TargetSession = _sessionCollectionService.Sessions.FirstOrDefault();
			}
		}
		else if ((e.Action == NotifyCollectionChangedAction.Add || e.Action == NotifyCollectionChangedAction.Replace) && TargetSession == null)
		{
			TargetSession = _sessionCollectionService.Sessions.FirstOrDefault();
		}

		UpdateCommandStates();
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
		}
		else if (e.PropertyName == nameof(SessionCollectionService.GlobalSelectedSession) && _targetSession == null)
		{
			var fallback = _sessionCollectionService.GlobalSelectedSession;
			if (fallback != null)
			{
				TargetSession = fallback;
			}
		}
	}

	private void OnTargetSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SessionModel.ScriptRuntimeStatus) ||
			e.PropertyName == nameof(SessionModel.ActiveScriptPath) ||
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

		_scriptService.AddOrUpdateScript(profile.FilePath, profile.Name, profile.Description);
		tracked = _scriptService.FindByPath(profile.FilePath);
		if (tracked != null)
		{
			tracked.HideFromRecents = false;
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

		addRange(_libraryScripts);
		addRange(_scriptService.RecentScripts);
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

		var inLibrary = _libraryScripts.Any(p => string.Equals(p.FilePath, script.FilePath, StringComparison.OrdinalIgnoreCase));
		var inRecents = _scriptService.RecentScripts.Any(p =>
			!p.HideFromRecents &&
			string.Equals(p.FilePath, script.FilePath, StringComparison.OrdinalIgnoreCase));
		var isFavorite = _favorites.Any(p => string.Equals(p.FilePath, script.FilePath, StringComparison.OrdinalIgnoreCase));
		var isMissing = !script.FileExists;

		return _selectedFilter switch
		{
			ScriptBrowserFilter.Library => inLibrary,
			ScriptBrowserFilter.Favorites => isFavorite,
			ScriptBrowserFilter.Recents => inRecents,
			ScriptBrowserFilter.Missing => isMissing,
			_ => true
		};
	}

	private void UpdateCommandStates()
	{
		LoadScriptCommand.NotifyCanExecuteChanged();
		ReloadScriptCommand.NotifyCanExecuteChanged();
		StopScriptCommand.NotifyCanExecuteChanged();
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

	public void Dispose()
	{
		_sessionCollectionService.PropertyChanged -= OnSessionCollectionPropertyChanged;
		_sessionCollectionService.Sessions.CollectionChanged -= OnSessionsCollectionChanged;
		if (_targetSession != null)
		{
			_targetSession.PropertyChanged -= OnTargetSessionPropertyChanged;
		}
		_scriptService.RecentScripts.CollectionChanged -= OnRecentScriptsChanged;
		_scriptService.RecentScripts.CollectionChanged -= OnRecentScriptsCollectionChanged;
		foreach (var profile in _scriptService.RecentScripts)
		{
			profile.PropertyChanged -= OnScriptProfilePropertyChanged;
		}
	}
}
