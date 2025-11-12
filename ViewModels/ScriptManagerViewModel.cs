using Microsoft.Win32;
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
	private ScriptProfile? _selectedScript;
	private string _hotReloadScriptPath = string.Empty;
	private SessionModel? _targetSession;

	/// <summary>
	/// Convenience constructor used by XAML design-time. Creates service instances on demand.
	/// </summary>
	public ScriptManagerViewModel()
		: this(new ScriptManagerService(), SessionCollectionService.Instance)
	{
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

		// Track session changes so tooling stays in sync when sessions open/close
		_sessionCollectionService.PropertyChanged += OnSessionCollectionPropertyChanged;
		_sessionCollectionService.Sessions.CollectionChanged += OnSessionsCollectionChanged;

		AddScriptCommand = new RelayCommand(AddScript);
		RemoveScriptCommand = new RelayCommand<ScriptProfile?>(RemoveScript, profile => profile != null);
		ToggleFavoriteCommand = new RelayCommand<ScriptProfile?>(ToggleFavorite, profile => profile != null);
		LoadScriptCommand = new RelayCommand<ScriptProfile?>(async profile => await LoadScriptAsync(profile, false), CanExecuteScriptCommand);
		ReloadScriptCommand = new RelayCommand<ScriptProfile?>(async profile => await LoadScriptAsync(profile, true), CanExecuteScriptCommand);
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
		RecentScriptsView?.Refresh();
	}

	public ICollectionView RecentScriptsView { get; private set; } = null!;

	public ObservableCollection<ScriptProfile> LibraryScripts => _libraryScripts;

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

			_targetSession = value;
			_sessionCollectionService.GlobalHotReloadTargetSession = value;
			OnPropertyChanged(nameof(TargetSession));
		}
	}

	public bool HasFavorites => _favorites.Count > 0;
	public bool HasSessions => _sessionCollectionService.Sessions.Count > 0;
	public bool HasLibraryScripts => _libraryScripts.Count > 0;
	public string LibraryDirectoryPath => _scriptService.DefaultLibraryPath;

	public IRelayCommand AddScriptCommand { get; }
	public IRelayCommand<ScriptProfile?> RemoveScriptCommand { get; }
	public IRelayCommand<ScriptProfile?> ToggleFavoriteCommand { get; }
	public IRelayCommand<ScriptProfile?> LoadScriptCommand { get; }
	public IRelayCommand<ScriptProfile?> ReloadScriptCommand { get; }
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
		var targetSession = TargetSession;
		if (targetSession == null)
		{
			var fallback = _sessionCollectionService.GlobalSelectedSession
				?? _sessionCollectionService.Sessions.FirstOrDefault();
			if (fallback != null)
			{
				TargetSession = fallback;
				targetSession = fallback;
			}
		}

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

		var pid = targetSession.RSProcess?.Id;

		var verb = isReload ? "reload" : "load";
		ConsoleLogService.Instance.Append(
			$"[ScriptManager] Requesting {verb} for '{tracked.Name}' to session '{targetSession.Name}' (PID {pid})",
			ConsoleLogSource.Orbit,
			ConsoleLogLevel.Info);

		var success = pid.HasValue
			? await OrbitCommandClient.SendReloadAsync(tracked.FilePath, pid.Value, CancellationToken.None)
			: await OrbitCommandClient.SendReloadAsync(tracked.FilePath, CancellationToken.None);

		if (!success)
		{
			ConsoleLogService.Instance.Append(
				$"[ScriptManager] Failed to send {verb} command for '{tracked.Name}'. Check if MESharp is running.",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
		}
		// Note: success=true only means the command was sent, not that the script loaded.
		// Check the MESharp console for actual script loading results.
	}

	private bool CanExecuteScriptCommand(ScriptProfile? profile)
	{
		if (profile?.FileExists == true)
		{
			return true;
		}

		return !string.IsNullOrWhiteSpace(HotReloadScriptPath) && File.Exists(HotReloadScriptPath);
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
	}

	private void OnSessionCollectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(SessionCollectionService.GlobalHotReloadTargetSession))
		{
			var sharedTarget = _sessionCollectionService.GlobalHotReloadTargetSession;
			if (!ReferenceEquals(_targetSession, sharedTarget))
			{
				_targetSession = sharedTarget;
				OnPropertyChanged(nameof(TargetSession));
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
		}
	}

	protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

	public void Dispose()
	{
		_sessionCollectionService.PropertyChanged -= OnSessionCollectionPropertyChanged;
		_sessionCollectionService.Sessions.CollectionChanged -= OnSessionsCollectionChanged;
		_scriptService.RecentScripts.CollectionChanged -= OnRecentScriptsChanged;
		_scriptService.RecentScripts.CollectionChanged -= OnRecentScriptsCollectionChanged;
		foreach (var profile in _scriptService.RecentScripts)
		{
			profile.PropertyChanged -= OnScriptProfilePropertyChanged;
		}
	}
}
