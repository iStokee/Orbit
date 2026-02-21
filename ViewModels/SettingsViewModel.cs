using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.IO;
using MahApps.Metro.IconPacks;
using Orbit;
using Orbit.Logging;
using Orbit.Services;
using Orbit.Services.Updates;
using Orbit.Utilities;
using Orbit.Models;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace Orbit.ViewModels
{
	/// <summary>
	/// ViewModel for the Settings view with auto-update functionality
	/// </summary>
	public class SettingsViewModel : INotifyPropertyChanged, IDisposable
	{
		private readonly GitHubReleaseChecker _releaseChecker = new GitHubReleaseChecker();
		private readonly UpdateManager _updateManager = new UpdateManager();
		private readonly CancellationTokenSource _lifetimeCts = new();
		private bool _disposed;

		private string _currentVersion;
		private string _availableVersion;
		private string _updateStatusText = "Click 'Check for Updates' to get started";
		private PackIconMaterialKind _updateStatusIcon = PackIconMaterialKind.InformationOutline;
		private bool _hasUpdate = false;
		private bool _isCheckingForUpdates = false;
		private bool _isDownloading = false;
		private bool _isDownloadIndeterminate = false;
		private int _downloadProgress = 0;
		private bool _hasUpdateError = false;
		private string _updateErrorMessage;
		private bool _canInstallUpdate = false;
		private DateTime? _lastChecked = null;
		private string _lastCheckedDisplay = "Never";

		private string _downloadedZipPath;
		private string _extractedFolderPath;

		private const string ExpectedGitHubAuthor = UpdateConfig.ExpectedAuthor;

		public SettingsViewModel()
		{
			// Get current version from AppVersion helper
			CurrentVersion = AppVersion.Display;

			FloatingMenuDirectionOptions = Enum.GetValues(typeof(FloatingMenuDirection));
			FloatingMenuQuickToggleModes = Enum.GetValues(typeof(FloatingMenuQuickToggleMode));

			// Commands
			CheckForUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
			InstallUpdateCommand = new RelayCommand(async () => await InstallUpdateAsync(), () => CanInstallUpdate);
			OpenThemeLogCommand = new RelayCommand(OpenThemeLog);
			ClearThemeLogCommand = new RelayCommand(ClearThemeLog);
			OpenOrbitInteractionLogCommand = new RelayCommand(OpenOrbitInteractionLog);
			ClearOrbitInteractionLogCommand = new RelayCommand(ClearOrbitInteractionLog);
			OpenToolsOverviewCommand = new RelayCommand(() => TryApplyToMain(vm => vm.OpenToolsOverviewTab()));
			ConfigureLauncherAccountsCommand = new RelayCommand(OpenLauncherAccountConfig);

			// Check for updates on startup (silently)
			_ = CheckForUpdatesAsync(silent: true);
		}

		#region Properties

		public string CurrentVersion
		{
			get => _currentVersion;
			set
			{
				_currentVersion = value;
				OnPropertyChanged();
			}
		}

		public string AvailableVersion
		{
			get => _availableVersion;
			set
			{
				_availableVersion = value;
				OnPropertyChanged();
			}
		}

		public string UpdateStatusText
		{
			get => _updateStatusText;
			set
			{
				_updateStatusText = value;
				OnPropertyChanged();
			}
		}

		public PackIconMaterialKind UpdateStatusIcon
		{
			get => _updateStatusIcon;
			set
			{
				_updateStatusIcon = value;
				OnPropertyChanged();
			}
		}

		public bool HasUpdate
		{
			get => _hasUpdate;
			set
			{
				_hasUpdate = value;
				OnPropertyChanged();
			}
		}

		public bool IsCheckingForUpdates
		{
			get => _isCheckingForUpdates;
			set
			{
				_isCheckingForUpdates = value;
				OnPropertyChanged();
			}
		}

		public bool IsDownloading
		{
			get => _isDownloading;
			set
			{
				_isDownloading = value;
				OnPropertyChanged();
			}
		}

		public bool IsDownloadIndeterminate
		{
			get => _isDownloadIndeterminate;
			set
			{
				_isDownloadIndeterminate = value;
				OnPropertyChanged();
			}
		}

		public int DownloadProgress
		{
			get => _downloadProgress;
			set
			{
				_downloadProgress = value;
				OnPropertyChanged();
			}
		}

		public bool HasUpdateError
		{
			get => _hasUpdateError;
			set
			{
				_hasUpdateError = value;
				OnPropertyChanged();
			}
		}

		public string UpdateErrorMessage
		{
			get => _updateErrorMessage;
			set
			{
				_updateErrorMessage = value;
				OnPropertyChanged();
			}
		}

		public bool CanInstallUpdate
		{
			get => _canInstallUpdate;
			set
			{
				if (_canInstallUpdate == value)
				{
					return;
				}

				_canInstallUpdate = value;
				OnPropertyChanged();
				InstallUpdateCommand.NotifyCanExecuteChanged();
			}
		}

		public string LastCheckedDisplay
		{
			get => _lastCheckedDisplay;
			set
			{
				_lastCheckedDisplay = value;
				OnPropertyChanged();
			}
		}

		#endregion

		#region Commands

		public IRelayCommand CheckForUpdatesCommand { get; }
		public IRelayCommand InstallUpdateCommand { get; }
		public IRelayCommand OpenThemeLogCommand { get; }
		public IRelayCommand ClearThemeLogCommand { get; }
		public IRelayCommand OpenOrbitInteractionLogCommand { get; }
		public IRelayCommand ClearOrbitInteractionLogCommand { get; }
		public IRelayCommand OpenToolsOverviewCommand { get; }
		public IRelayCommand ConfigureLauncherAccountsCommand { get; }

		#endregion

		#region Methods

		private async Task CheckForUpdatesAsync(bool silent = false)
		{
			if (_disposed || IsCheckingForUpdates)
			{
				return;
			}

			try
			{
				IsCheckingForUpdates = true;
				HasUpdateError = false;
				UpdateErrorMessage = null;

				ConsoleLogService.Instance.Append(
					$"[Update] Checking for updates... (Current: v{CurrentVersion})",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				if (!silent)
				{
					UpdateStatusText = "Checking for updates...";
					UpdateStatusIcon = PackIconMaterialKind.CloudSearch;
				}

				ConsoleLogService.Instance.Append(
					$"[Update] Querying GitHub API: {UpdateConfig.Owner}/{UpdateConfig.Repo}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);

				var updateInfo = await _releaseChecker.CheckAsync(
					expectedAssetName: UpdateConfig.DefaultAssetName,
					expectedAuthor: ExpectedGitHubAuthor,
					includePrereleases: false,
					cancellationToken: _lifetimeCts.Token
				);
				if (_disposed || _lifetimeCts.IsCancellationRequested)
				{
					return;
				}

				// Update last checked timestamp
				_lastChecked = DateTime.Now;
				UpdateLastCheckedDisplay();

				if (!string.IsNullOrEmpty(updateInfo.ErrorMessage))
				{
					ConsoleLogService.Instance.Append(
						$"[Update] Check failed: {updateInfo.ErrorMessage}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Warning);

					if (!silent)
					{
						UpdateStatusText = "Failed to check for updates";
						UpdateStatusIcon = PackIconMaterialKind.AlertCircleOutline;
						HasUpdateError = true;
						UpdateErrorMessage = updateInfo.ErrorMessage;
					}
					CanInstallUpdate = false;
					return;
				}

				ConsoleLogService.Instance.Append(
					$"[Update] GitHub API response received successfully",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);

				if (updateInfo.HasUpdate)
				{
					HasUpdate = true;
					AvailableVersion = updateInfo.RemoteVersion.ToString();
					UpdateStatusText = $"Update available: v{AvailableVersion}";
					UpdateStatusIcon = PackIconMaterialKind.Update;
					CanInstallUpdate = true;

					ConsoleLogService.Instance.Append(
						$"[Update] ✓ New version available: v{AvailableVersion} (Current: v{CurrentVersion})",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
					ConsoleLogService.Instance.Append(
						$"[Update]   Download URL: {updateInfo.DownloadUrl}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Debug);
					ConsoleLogService.Instance.Append(
						$"[Update]   Release Author: {updateInfo.ReleaseAuthor}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Debug);
				}
				else
				{
					HasUpdate = false;
					CanInstallUpdate = false;
					UpdateStatusText = "You're up to date!";
					UpdateStatusIcon = PackIconMaterialKind.CheckCircleOutline;

					ConsoleLogService.Instance.Append(
						$"[Update] ✓ You're running the latest version (v{CurrentVersion})",
						ConsoleLogSource.Orbit,
						silent ? ConsoleLogLevel.Debug : ConsoleLogLevel.Info);
				}
			}
			catch (OperationCanceledException)
			{
				// View disposed or operation canceled.
			}
			catch (Exception ex)
			{
				ConsoleLogService.Instance.Append(
					$"[Update] ✗ Exception during update check: {ex.GetType().Name}: {ex.Message}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Error);

				if (ex.InnerException != null)
				{
					ConsoleLogService.Instance.Append(
						$"[Update]   Inner: {ex.InnerException.Message}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Error);
				}

				if (!silent)
				{
					UpdateStatusText = "Error checking for updates";
					UpdateStatusIcon = PackIconMaterialKind.AlertCircleOutline;
					HasUpdateError = true;
					UpdateErrorMessage = $"Failed to check for updates: {ex.Message}";
				}
				CanInstallUpdate = false;
			}
			finally
			{
				if (!_disposed)
				{
					IsCheckingForUpdates = false;
				}
			}
		}

		private void UpdateLastCheckedDisplay()
		{
			if (!_lastChecked.HasValue)
			{
				LastCheckedDisplay = "Never";
				return;
			}

			var now = DateTime.Now;
			var diff = now - _lastChecked.Value;

			if (diff.TotalSeconds < 60)
			{
				LastCheckedDisplay = "Just now";
			}
			else if (diff.TotalMinutes < 60)
			{
				var mins = (int)diff.TotalMinutes;
				LastCheckedDisplay = $"{mins} minute{(mins == 1 ? "" : "s")} ago";
			}
			else if (diff.TotalHours < 24)
			{
				var hours = (int)diff.TotalHours;
				LastCheckedDisplay = $"{hours} hour{(hours == 1 ? "" : "s")} ago";
			}
			else
			{
				LastCheckedDisplay = _lastChecked.Value.ToString("MMM d, yyyy h:mm tt");
			}
		}

		private async Task InstallUpdateAsync()
		{
			if (_disposed || !HasUpdate || IsDownloading)
			{
				return;
			}

			try
			{
				IsDownloading = true;
				IsDownloadIndeterminate = true;
				DownloadProgress = 0;
				HasUpdateError = false;
				UpdateErrorMessage = null;
				CanInstallUpdate = false;

				UpdateStatusText = "Downloading update...";
				UpdateStatusIcon = PackIconMaterialKind.Download;

				ConsoleLogService.Instance.Append(
					$"[Update] ═══════════════════════════════════════",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);
				ConsoleLogService.Instance.Append(
					$"[Update] Starting update installation process",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);
				ConsoleLogService.Instance.Append(
					$"[Update] Target version: v{AvailableVersion}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				// Get the latest release info again to ensure we have the download URL
				ConsoleLogService.Instance.Append(
					$"[Update] Fetching release metadata from GitHub...",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);

				var updateInfo = await _releaseChecker.CheckAsync(
					expectedAssetName: UpdateConfig.DefaultAssetName,
					expectedAuthor: ExpectedGitHubAuthor,
					cancellationToken: _lifetimeCts.Token
				);
				if (_disposed || _lifetimeCts.IsCancellationRequested)
				{
					return;
				}

				if (!string.IsNullOrWhiteSpace(updateInfo.ErrorMessage))
				{
					throw new Exception(updateInfo.ErrorMessage);
				}

				if (!updateInfo.HasUpdate || string.IsNullOrEmpty(updateInfo.DownloadUrl))
				{
					throw new Exception("Update information is no longer available. Please try checking for updates again.");
				}

				ConsoleLogService.Instance.Append(
					$"[Update] ✓ Release metadata fetched",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);
				ConsoleLogService.Instance.Append(
					$"[Update]   Asset: {updateInfo.AssetName}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);
				ConsoleLogService.Instance.Append(
					$"[Update]   Download URL: {updateInfo.DownloadUrl}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);

				// Download with progress reporting
				IsDownloadIndeterminate = false;
				ConsoleLogService.Instance.Append(
					$"[Update] Beginning download...",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				var progress = new Progress<int>(percent =>
				{
					DownloadProgress = percent;
					if (percent % 10 == 0) // Log every 10%
					{
						ConsoleLogService.Instance.Append(
							$"[Update]   Download progress: {percent}%",
							ConsoleLogSource.Orbit,
							ConsoleLogLevel.Debug);
					}
				});

				_downloadedZipPath = await _updateManager.DownloadUpdateAsync(
					updateInfo.DownloadUrl,
					updateInfo.AssetName,
					progress,
					_lifetimeCts.Token
				);
				if (_disposed || _lifetimeCts.IsCancellationRequested)
				{
					return;
				}

				ConsoleLogService.Instance.Append(
					$"[Update] ✓ Download complete: {_downloadedZipPath}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				UpdateStatusText = "Extracting update...";
				ConsoleLogService.Instance.Append(
					$"[Update] Extracting update package...",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				// Extract the update
				_extractedFolderPath = _updateManager.ExtractUpdate(_downloadedZipPath);

				ConsoleLogService.Instance.Append(
					$"[Update] ✓ Extraction complete: {_extractedFolderPath}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				UpdateStatusText = "Ready to install";
				UpdateStatusIcon = PackIconMaterialKind.CheckCircle;

				ConsoleLogService.Instance.Append(
					$"[Update] Update package is ready for installation",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);

				// Show confirmation dialog
				ConsoleLogService.Instance.Append(
					$"[Update] Prompting user for confirmation...",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);

				var result = System.Windows.MessageBox.Show(
					$"Update v{AvailableVersion} has been downloaded and is ready to install.\n\n" +
					"Orbit will close and restart automatically to complete the update.\n\n" +
					"Do you want to install the update now?",
					"Install Update",
					MessageBoxButton.YesNo,
					MessageBoxImage.Question
				);

				if (result == MessageBoxResult.Yes)
				{
					ConsoleLogService.Instance.Append(
						$"[Update] User confirmed installation",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
					ConsoleLogService.Instance.Append(
						$"[Update] Launching updater helper (Orbit.Updater.exe)...",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
					ConsoleLogService.Instance.Append(
						$"[Update] Source: {_extractedFolderPath}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Debug);
					ConsoleLogService.Instance.Append(
						$"[Update] Target: {AppContext.BaseDirectory}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Debug);
					ConsoleLogService.Instance.Append(
						$"[Update] Orbit will now close and restart automatically",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
					ConsoleLogService.Instance.Append(
						$"[Update] ═══════════════════════════════════════",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);

					// Small delay to ensure log is written
					await Task.Delay(500);

					// Launch updater and exit
					_updateManager.LaunchUpdaterAndExit(_extractedFolderPath);
				}
				else
				{
					CanInstallUpdate = true;
					ConsoleLogService.Instance.Append(
						$"[Update] User postponed installation",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
					ConsoleLogService.Instance.Append(
						$"[Update] Update remains downloaded and can be installed later",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
					ConsoleLogService.Instance.Append(
						$"[Update] ═══════════════════════════════════════",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Info);
				}
			}
			catch (OperationCanceledException)
			{
				// View disposed or operation canceled.
			}
			catch (Exception ex)
			{
				UpdateStatusText = "Update failed";
				UpdateStatusIcon = PackIconMaterialKind.AlertCircleOutline;
				HasUpdateError = true;
				UpdateErrorMessage = $"Failed to install update: {ex.Message}";

				ConsoleLogService.Instance.Append(
					$"[Update] ✗ Update installation FAILED",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Error);
				ConsoleLogService.Instance.Append(
					$"[Update]   Error: {ex.GetType().Name}: {ex.Message}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Error);
				if (ex.InnerException != null)
				{
					ConsoleLogService.Instance.Append(
						$"[Update]   Inner: {ex.InnerException.Message}",
						ConsoleLogSource.Orbit,
						ConsoleLogLevel.Error);
				}
				ConsoleLogService.Instance.Append(
					$"[Update]   Stack trace: {ex.StackTrace}",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Error);
				ConsoleLogService.Instance.Append(
					$"[Update] ═══════════════════════════════════════",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Info);
			}
			finally
			{
				if (!_disposed)
				{
					IsDownloading = false;
					IsDownloadIndeterminate = false;
					DownloadProgress = 0;
				}
			}
		}

	#endregion

	#region Session Settings

	public bool AutoInjectOnReady
	{
		get => Settings.Default.AutoInjectOnReady;
		set
		{
			ApplyAndPersist(
				Settings.Default.AutoInjectOnReady,
				value,
				v => Settings.Default.AutoInjectOnReady = v,
				nameof(AutoInjectOnReady),
				(vm, v) => vm.AutoInjectOnReady = v);
		}
	}

	#endregion

	#region Floating Menu Settings

	public Array FloatingMenuDirectionOptions { get; }
	public Array FloatingMenuQuickToggleModes { get; }

	public bool ShowFloatingMenuAutoShow
	{
		get => Settings.Default.ShowFloatingMenuAutoShow;
		set
		{
			ApplyAndPersist(
				Settings.Default.ShowFloatingMenuAutoShow,
				value,
				v => Settings.Default.ShowFloatingMenuAutoShow = v,
				nameof(ShowFloatingMenuAutoShow));
		}
	}

	public bool ShowFloatingMenuOnHome
	{
		get => Settings.Default.ShowFloatingMenuOnHome;
		set
		{
			ApplyAndPersist(
				Settings.Default.ShowFloatingMenuOnHome,
				value,
				v => Settings.Default.ShowFloatingMenuOnHome = v,
				nameof(ShowFloatingMenuOnHome),
				(vm, v) => vm.ShowFloatingMenuOnHome = v);
		}
	}

	public bool ShowFloatingMenuOnSessionTabs
	{
		get => Settings.Default.ShowFloatingMenuOnSessionTabs;
		set
		{
			ApplyAndPersist(
				Settings.Default.ShowFloatingMenuOnSessionTabs,
				value,
				v => Settings.Default.ShowFloatingMenuOnSessionTabs = v,
				nameof(ShowFloatingMenuOnSessionTabs),
				(vm, v) => vm.ShowFloatingMenuOnSessionTabs = v);
		}
	}

	public bool ShowFloatingMenuOnToolTabs
	{
		get => Settings.Default.ShowFloatingMenuOnToolTabs;
		set
		{
			ApplyAndPersist(
				Settings.Default.ShowFloatingMenuOnToolTabs,
				value,
				v => Settings.Default.ShowFloatingMenuOnToolTabs = v,
				nameof(ShowFloatingMenuOnToolTabs),
				(vm, v) => vm.ShowFloatingMenuOnToolTabs = v);
		}
	}

	public double FloatingMenuOpacity
	{
		get
		{
			var stored = Settings.Default.FloatingMenuOpacity;
			return stored <= 0 ? 0.95 : stored;
		}
		set
		{
			var normalized = Math.Clamp(value, 0.3, 1);
			if (Math.Abs(FloatingMenuOpacity - normalized) < 0.01)
				return;

			ApplyAndPersist(
				Settings.Default.FloatingMenuOpacity,
				normalized,
				v => Settings.Default.FloatingMenuOpacity = v,
				nameof(FloatingMenuOpacity),
				(vm, v) => vm.FloatingMenuOpacity = v);
		}
	}

	public double FloatingMenuBackgroundOpacity
	{
		get
		{
			var stored = Settings.Default.FloatingMenuBackgroundOpacity;
			return stored <= 0 ? 0.9 : stored;
		}
		set
		{
			var normalized = Math.Clamp(value, 0.2, 1);
			if (Math.Abs(FloatingMenuBackgroundOpacity - normalized) < 0.01)
				return;

			ApplyAndPersist(
				Settings.Default.FloatingMenuBackgroundOpacity,
				normalized,
				v => Settings.Default.FloatingMenuBackgroundOpacity = v,
				nameof(FloatingMenuBackgroundOpacity),
				(vm, v) => vm.FloatingMenuBackgroundOpacity = v);
		}
	}

	public double FloatingMenuInactivitySeconds
	{
		get
		{
			var stored = Settings.Default.FloatingMenuInactivitySeconds;
			return stored <= 0 ? 2 : stored;
		}
		set
		{
			var normalized = Math.Clamp(value, 0.5, 8);
			if (Math.Abs(FloatingMenuInactivitySeconds - normalized) < 0.05)
				return;

			ApplyAndPersist(
				Settings.Default.FloatingMenuInactivitySeconds,
				normalized,
				v => Settings.Default.FloatingMenuInactivitySeconds = v,
				nameof(FloatingMenuInactivitySeconds),
				(vm, v) => vm.FloatingMenuInactivitySeconds = v);
		}
	}

	public bool FloatingMenuAutoDirection
	{
		get => Settings.Default.FloatingMenuAutoDirection;
		set
		{
			ApplyAndPersist(
				Settings.Default.FloatingMenuAutoDirection,
				value,
				v => Settings.Default.FloatingMenuAutoDirection = v,
				nameof(FloatingMenuAutoDirection),
				(vm, v) => vm.FloatingMenuAutoDirection = v);
		}
	}

	public FloatingMenuDirection FloatingMenuDirection
	{
		get
		{
			var stored = Settings.Default.FloatingMenuDirection;
			return Enum.TryParse(stored, out FloatingMenuDirection parsed)
				? parsed
				: FloatingMenuDirection.Right;
		}
		set
		{
			ApplyAndPersist(
				FloatingMenuDirection,
				value,
				v => Settings.Default.FloatingMenuDirection = v.ToString(),
				nameof(FloatingMenuDirection),
				(vm, v) => vm.FloatingMenuDirection = v);
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

			ApplyAndPersist(
				Settings.Default.FloatingMenuDockEdgeThreshold,
				normalized,
				v => Settings.Default.FloatingMenuDockEdgeThreshold = v,
				nameof(FloatingMenuDockEdgeThreshold),
				(vm, v) => vm.FloatingMenuDockEdgeThreshold = v);
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

			ApplyAndPersist(
				Settings.Default.FloatingMenuDockCornerThreshold,
				normalized,
				v => Settings.Default.FloatingMenuDockCornerThreshold = v,
				nameof(FloatingMenuDockCornerThreshold),
				(vm, v) => vm.FloatingMenuDockCornerThreshold = v,
				() => OnPropertyChanged(nameof(FloatingMenuDockCornerRadius)));
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

			ApplyAndPersist(
				Settings.Default.FloatingMenuDockCornerHeight,
				normalized,
				v => Settings.Default.FloatingMenuDockCornerHeight = v,
				nameof(FloatingMenuDockCornerHeight),
				(vm, v) => vm.FloatingMenuDockCornerHeight = v,
				() => OnPropertyChanged(nameof(FloatingMenuDockCornerRadius)));
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

			ApplyAndPersist(
				Settings.Default.FloatingMenuDockCornerRoundness,
				normalized,
				v => Settings.Default.FloatingMenuDockCornerRoundness = v,
				nameof(FloatingMenuDockCornerRoundness),
				(vm, v) => vm.FloatingMenuDockCornerRoundness = v,
				() => OnPropertyChanged(nameof(FloatingMenuDockCornerRadius)));
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

			ApplyAndPersist(
				Settings.Default.FloatingMenuDockEdgeCoverage,
				normalized,
				v => Settings.Default.FloatingMenuDockEdgeCoverage = v,
				nameof(FloatingMenuDockEdgeCoverage),
				(vm, v) => vm.FloatingMenuDockEdgeCoverage = v);
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

			ApplyAndPersist(
				Settings.Default.FloatingMenuDockZoneOpacity,
				normalized,
				v => Settings.Default.FloatingMenuDockZoneOpacity = v,
				nameof(FloatingMenuDockZoneOpacity),
				(vm, v) => vm.FloatingMenuDockZoneOpacity = v);
		}
	}

	public FloatingMenuQuickToggleMode FloatingMenuQuickToggleMode
	{
		get => ParseFloatingMenuQuickToggleMode(Settings.Default.FloatingMenuQuickToggle);
		set
		{
			ApplyAndPersist(
				FloatingMenuQuickToggleMode,
				value,
				v => Settings.Default.FloatingMenuQuickToggle = v.ToString(),
				nameof(FloatingMenuQuickToggleMode),
				(vm, v) => vm.FloatingMenuQuickToggleMode = v);
		}
	}

	public bool ShowAllSnapZonesOnClip
	{
		get => Settings.Default.FloatingMenuShowAllSnapZonesOnClip;
		set
		{
			ApplyAndPersist(
				Settings.Default.FloatingMenuShowAllSnapZonesOnClip,
				value,
				v => Settings.Default.FloatingMenuShowAllSnapZonesOnClip = v,
				nameof(ShowAllSnapZonesOnClip),
				(vm, v) => vm.ShowAllSnapZonesOnClip = v);
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

	#endregion

	#region Theme & Logging Settings

	public bool IsThemeLoggingEnabled
	{
		get => ThemeLogger.IsEnabled;
		set
		{
			if (ThemeLogger.IsEnabled == value) return;
			ThemeLogger.IsEnabled = value;
			OnPropertyChanged();
		}
	}

	public string ThemeLogFilePath => ThemeLogger.LogFilePath;

	public bool IsOrbitInteractionLoggingEnabled
	{
		get => Settings.Default.OrbitInteractionLoggingEnabled;
		set
		{
			if (Settings.Default.OrbitInteractionLoggingEnabled == value)
			{
				return;
			}

			Settings.Default.OrbitInteractionLoggingEnabled = value;
			Settings.Default.Save();
			OrbitInteractionLogger.IsEnabled = value;
			OnPropertyChanged();
			OnPropertyChanged(nameof(OrbitInteractionLogFilePath));
		}
	}

	public string OrbitInteractionLogFilePath => OrbitInteractionLogger.LogFilePath;

	public bool ShowThemeManagerWelcomeMessage
	{
		get => Settings.Default.ShowThemeManagerWelcomeMessage;
		set
		{
			ApplyAndPersist(
				Settings.Default.ShowThemeManagerWelcomeMessage,
				value,
				v => Settings.Default.ShowThemeManagerWelcomeMessage = v,
				nameof(ShowThemeManagerWelcomeMessage),
				(vm, v) => vm.UpdateFloatingMenuWelcomeHint(v));
		}
	}

	#endregion

	#region Helpers

	private static bool TryApplyToMain(Action<MainWindowViewModel> action)
	{
		var app = Application.Current;
		if (app == null)
			return false;

		var handled = false;

		void Execute()
		{
			foreach (var vm in app.Windows
				.OfType<Window>()
				.Select(w => w.DataContext)
				.OfType<MainWindowViewModel>()
				.Distinct())
			{
				action(vm);
				handled = true;
			}
		}

		var dispatcher = app.Dispatcher;
		if (dispatcher?.CheckAccess() == true)
		{
			Execute();
		}
		else
		{
			dispatcher?.Invoke((Action)Execute);
		}

		return handled;
	}

	private static FloatingMenuQuickToggleMode ParseFloatingMenuQuickToggleMode(string value)
	{
		if (!string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, out FloatingMenuQuickToggleMode mode))
		{
			return mode;
		}

		return FloatingMenuQuickToggleMode.MiddleMouse;
	}

	private void OpenThemeLog()
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
	}

	private void ClearThemeLog()
	{
		ThemeLogger.ClearLog();
		MessageBox.Show("Theme log cleared successfully.",
			"Log Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
		OnPropertyChanged(nameof(ThemeLogFilePath));
	}

	private void OpenOrbitInteractionLog()
	{
		try
		{
			var filePath = OrbitInteractionLogger.LogFilePath;
			if (File.Exists(filePath))
			{
				System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
				{
					FileName = filePath,
					UseShellExecute = true
				});
			}
			else
			{
				MessageBox.Show("Interaction log file does not exist yet. Enable logging and drag tabs/windows in Orbit View first.",
					"Log File Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
			}
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Failed to open interaction log file: {ex.Message}",
				"Error", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	private void ClearOrbitInteractionLog()
	{
		OrbitInteractionLogger.ClearLog();
		MessageBox.Show("Orbit interaction log cleared successfully.",
			"Log Cleared", MessageBoxButton.OK, MessageBoxImage.Information);
		OnPropertyChanged(nameof(OrbitInteractionLogFilePath));
	}

	private void OpenLauncherAccountConfig()
	{
		try
		{
			var owner = Application.Current?.Windows
				.OfType<Window>()
				.FirstOrDefault(w => w.IsActive);

			var window = new Orbit.Views.LauncherAccountConfigWindow();
			if (owner != null)
			{
				window.Owner = owner;
			}

			window.ShowDialog();
		}
		catch (Exception ex)
		{
			MessageBox.Show($"Failed to open launcher account config: {ex.Message}",
				"Orbit", MessageBoxButton.OK, MessageBoxImage.Error);
		}
	}

	#endregion

	#region Orbit View Settings

		/// <summary>
		/// Where new sessions should dock when launched
		/// </summary>
			public string SessionLaunchBehavior
			{
				get => Settings.Default.SessionLaunchBehavior;
				set
				{
					ApplyAndPersist(
						Settings.Default.SessionLaunchBehavior,
						value,
						v => Settings.Default.SessionLaunchBehavior = v,
						nameof(SessionLaunchBehavior));
				}
			}

			public string ClientLaunchMode
			{
				get => Settings.Default.ClientLaunchMode;
				set
				{
					ApplyAndPersist(
						Settings.Default.ClientLaunchMode,
						value,
						v => Settings.Default.ClientLaunchMode = v,
						nameof(ClientLaunchMode));
				}
			}

		/// <summary>
		/// Grid density for Orbit View (1=Single, 2=Standard, 3=Dense)
		/// </summary>
			public int OrbitViewGridDensity
			{
				get => Settings.Default.OrbitViewGridDensity;
				set
				{
					ApplyAndPersist(
						Settings.Default.OrbitViewGridDensity,
						value,
						v => Settings.Default.OrbitViewGridDensity = v,
						nameof(OrbitViewGridDensity));
				}
			}

		/// <summary>
		/// UI spacing compactness (0=Minimal, 1=Moderate, 2=Maximum)
		/// </summary>
			public int OrbitViewCompactness
			{
				get => Settings.Default.OrbitViewCompactness;
				set
				{
					ApplyAndPersist(
						Settings.Default.OrbitViewCompactness,
						value,
						v => Settings.Default.OrbitViewCompactness = v,
						nameof(OrbitViewCompactness));
				}
			}

		/// <summary>
		/// Tab header size (0=Compact, 1=Standard, 2=Comfortable)
		/// </summary>
			public int OrbitViewTabHeaderSize
			{
				get => Settings.Default.OrbitViewTabHeaderSize;
				set
				{
					ApplyAndPersist(
						Settings.Default.OrbitViewTabHeaderSize,
						value,
						v => Settings.Default.OrbitViewTabHeaderSize = v,
						nameof(OrbitViewTabHeaderSize));
				}
			}

		/// <summary>
		/// Border thickness for grid cells (0=None, 1=Minimal, 2=Standard)
		/// </summary>
			public int OrbitViewBorderThickness
			{
				get => Settings.Default.OrbitViewBorderThickness;
				set
				{
					ApplyAndPersist(
						Settings.Default.OrbitViewBorderThickness,
						value,
						v => Settings.Default.OrbitViewBorderThickness = v,
						nameof(OrbitViewBorderThickness));
				}
			}

		/// <summary>
		/// Auto-open Orbit View on startup
		/// </summary>
			public bool AutoOpenOrbitViewOnStartup
			{
				get => Settings.Default.AutoOpenOrbitViewOnStartup;
				set
				{
					ApplyAndPersist(
						Settings.Default.AutoOpenOrbitViewOnStartup,
						value,
						v => Settings.Default.AutoOpenOrbitViewOnStartup = v,
						nameof(AutoOpenOrbitViewOnStartup));
				}
			}

		#endregion

		#region INotifyPropertyChanged

		public event PropertyChangedEventHandler PropertyChanged;

	protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
		=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		#endregion

		private void ApplyAndPersist<T>(
			T currentValue,
			T newValue,
			Action<T> assignSetting,
			string propertyName,
			Action<MainWindowViewModel, T>? applyToWindow = null,
			Action? afterApply = null)
		{
			if (EqualityComparer<T>.Default.Equals(currentValue, newValue))
			{
				return;
			}

			var handled = false;
			if (applyToWindow != null)
			{
				handled = TryApplyToMain(vm => applyToWindow(vm, newValue));
			}

			if (!handled)
			{
				assignSetting(newValue);
			}

			Settings.Default.Save();
			OnPropertyChanged(propertyName);
			afterApply?.Invoke();
		}

		public void Dispose()
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			_lifetimeCts.Cancel();
			_lifetimeCts.Dispose();
		}
	}
}
