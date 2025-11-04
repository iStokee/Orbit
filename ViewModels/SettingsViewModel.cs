using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.IO;
using MahApps.Metro.IconPacks;
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
	public class SettingsViewModel : INotifyPropertyChanged
	{
		private readonly GitHubReleaseChecker _releaseChecker = new GitHubReleaseChecker();
		private readonly UpdateManager _updateManager = new UpdateManager();

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

		// TODO: Replace with your GitHub username
		private const string ExpectedGitHubAuthor = "iStokee";

		public SettingsViewModel()
		{
			// Get current version from assembly
			var version = Assembly.GetEntryAssembly()?.GetName()?.Version;
			CurrentVersion = version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "Unknown";

			FloatingMenuDirectionOptions = Enum.GetValues(typeof(FloatingMenuDirection));
			FloatingMenuQuickToggleModes = Enum.GetValues(typeof(FloatingMenuQuickToggleMode));

			// Commands
			CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync());
			InstallUpdateCommand = new RelayCommand(async _ => await InstallUpdateAsync(), _ => CanInstallUpdate);
			OpenThemeLogCommand = new RelayCommand(_ => OpenThemeLog());
			ClearThemeLogCommand = new RelayCommand(_ => ClearThemeLog());
			OpenToolsOverviewCommand = new RelayCommand(_ => TryApplyToMain(vm => vm.OpenToolsOverviewTab()));

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
				_canInstallUpdate = value;
				OnPropertyChanged();
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

		public ICommand CheckForUpdatesCommand { get; }
		public ICommand InstallUpdateCommand { get; }
		public ICommand OpenThemeLogCommand { get; }
		public ICommand ClearThemeLogCommand { get; }
		public ICommand OpenToolsOverviewCommand { get; }

		#endregion

		#region Methods

		private async Task CheckForUpdatesAsync(bool silent = false)
		{
			if (IsCheckingForUpdates) return;

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
					$"[Update] Querying GitHub API: {ExpectedGitHubAuthor}/Orbit",
					ConsoleLogSource.Orbit,
					ConsoleLogLevel.Debug);

				var updateInfo = await _releaseChecker.CheckAsync(
					expectedAssetName: "orbit-win-x64.zip",
					expectedAuthor: ExpectedGitHubAuthor,
					includePrereleases: false
				);

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
					AvailableVersion = $"{updateInfo.RemoteVersion.Major}.{updateInfo.RemoteVersion.Minor}.{updateInfo.RemoteVersion.Build}";
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
				IsCheckingForUpdates = false;
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
			if (!HasUpdate || IsDownloading) return;

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
					expectedAssetName: "orbit-win-x64.zip",
					expectedAuthor: ExpectedGitHubAuthor
				);

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
					progress
				);

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
				IsDownloading = false;
				IsDownloadIndeterminate = false;
				DownloadProgress = 0;
			}
		}

	#endregion

	#region Session Settings

	public bool AutoInjectOnReady
	{
		get => Settings.Default.AutoInjectOnReady;
		set
		{
			if (Settings.Default.AutoInjectOnReady == value) return;
			if (!TryApplyToMain(vm => vm.AutoInjectOnReady = value))
			{
				Settings.Default.AutoInjectOnReady = value;
			}
			OnPropertyChanged();
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
			if (Settings.Default.ShowFloatingMenuAutoShow == value) return;
			Settings.Default.ShowFloatingMenuAutoShow = value;
			Settings.Default.Save();
			OnPropertyChanged();
		}
	}

	public bool ShowFloatingMenuOnHome
	{
		get => Settings.Default.ShowFloatingMenuOnHome;
		set
		{
			if (Settings.Default.ShowFloatingMenuOnHome == value) return;
			if (!TryApplyToMain(vm => vm.ShowFloatingMenuOnHome = value))
			{
				Settings.Default.ShowFloatingMenuOnHome = value;
			}
			OnPropertyChanged();
		}
	}

	public bool ShowFloatingMenuOnSessionTabs
	{
		get => Settings.Default.ShowFloatingMenuOnSessionTabs;
		set
		{
			if (Settings.Default.ShowFloatingMenuOnSessionTabs == value) return;
			if (!TryApplyToMain(vm => vm.ShowFloatingMenuOnSessionTabs = value))
			{
				Settings.Default.ShowFloatingMenuOnSessionTabs = value;
			}
			OnPropertyChanged();
		}
	}

	public bool ShowFloatingMenuOnToolTabs
	{
		get => Settings.Default.ShowFloatingMenuOnToolTabs;
		set
		{
			if (Settings.Default.ShowFloatingMenuOnToolTabs == value) return;
			if (!TryApplyToMain(vm => vm.ShowFloatingMenuOnToolTabs = value))
			{
				Settings.Default.ShowFloatingMenuOnToolTabs = value;
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuOpacity = normalized))
			{
				Settings.Default.FloatingMenuOpacity = normalized;
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuBackgroundOpacity = normalized))
			{
				Settings.Default.FloatingMenuBackgroundOpacity = normalized;
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuInactivitySeconds = normalized))
			{
				Settings.Default.FloatingMenuInactivitySeconds = normalized;
			}
			OnPropertyChanged();
		}
	}

	public bool FloatingMenuAutoDirection
	{
		get => Settings.Default.FloatingMenuAutoDirection;
		set
		{
			if (Settings.Default.FloatingMenuAutoDirection == value) return;
			if (!TryApplyToMain(vm => vm.FloatingMenuAutoDirection = value))
			{
				Settings.Default.FloatingMenuAutoDirection = value;
			}
			OnPropertyChanged();
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
			if (FloatingMenuDirection == value) return;
			if (!TryApplyToMain(vm => vm.FloatingMenuDirection = value))
			{
				Settings.Default.FloatingMenuDirection = value.ToString();
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuDockEdgeThreshold = normalized))
			{
				Settings.Default.FloatingMenuDockEdgeThreshold = normalized;
				Settings.Default.Save();
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuDockCornerThreshold = normalized))
			{
				Settings.Default.FloatingMenuDockCornerThreshold = normalized;
				Settings.Default.Save();
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuDockCornerHeight = normalized))
			{
				Settings.Default.FloatingMenuDockCornerHeight = normalized;
				Settings.Default.Save();
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuDockCornerRoundness = normalized))
			{
				Settings.Default.FloatingMenuDockCornerRoundness = normalized;
				Settings.Default.Save();
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuDockEdgeCoverage = normalized))
			{
				Settings.Default.FloatingMenuDockEdgeCoverage = normalized;
				Settings.Default.Save();
			}
			OnPropertyChanged();
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
			if (!TryApplyToMain(vm => vm.FloatingMenuDockZoneOpacity = normalized))
			{
				Settings.Default.FloatingMenuDockZoneOpacity = normalized;
				Settings.Default.Save();
			}
			OnPropertyChanged();
		}
	}

	public FloatingMenuQuickToggleMode FloatingMenuQuickToggleMode
	{
		get => ParseFloatingMenuQuickToggleMode(Settings.Default.FloatingMenuQuickToggle);
		set
		{
			if (FloatingMenuQuickToggleMode == value) return;
			if (!TryApplyToMain(vm => vm.FloatingMenuQuickToggleMode = value))
			{
				Settings.Default.FloatingMenuQuickToggle = value.ToString();
				Settings.Default.Save();
			}
			OnPropertyChanged();
		}
	}

	public bool ShowAllSnapZonesOnClip
	{
		get => Settings.Default.FloatingMenuShowAllSnapZonesOnClip;
		set
		{
			if (Settings.Default.FloatingMenuShowAllSnapZonesOnClip == value) return;
			if (!TryApplyToMain(vm => vm.ShowAllSnapZonesOnClip = value))
			{
				Settings.Default.FloatingMenuShowAllSnapZonesOnClip = value;
				Settings.Default.Save();
			}
			OnPropertyChanged();
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

	public bool ShowThemeManagerWelcomeMessage
	{
		get => Settings.Default.ShowThemeManagerWelcomeMessage;
		set
		{
			if (Settings.Default.ShowThemeManagerWelcomeMessage == value) return;
			Settings.Default.ShowThemeManagerWelcomeMessage = value;
			TryApplyToMain(vm => vm.UpdateFloatingMenuWelcomeHint(value));
			Settings.Default.Save();
			OnPropertyChanged();
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
			if (app.MainWindow?.DataContext is MainWindowViewModel vm)
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
				if (Settings.Default.SessionLaunchBehavior == value) return;
				Settings.Default.SessionLaunchBehavior = value;
				Settings.Default.Save();
				OnPropertyChanged();
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
				if (Settings.Default.OrbitViewGridDensity == value) return;
				Settings.Default.OrbitViewGridDensity = value;
				Settings.Default.Save();
				OnPropertyChanged();
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
				if (Settings.Default.OrbitViewCompactness == value) return;
				Settings.Default.OrbitViewCompactness = value;
				Settings.Default.Save();
				OnPropertyChanged();
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
				if (Settings.Default.OrbitViewTabHeaderSize == value) return;
				Settings.Default.OrbitViewTabHeaderSize = value;
				Settings.Default.Save();
				OnPropertyChanged();
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
				if (Settings.Default.OrbitViewBorderThickness == value) return;
				Settings.Default.OrbitViewBorderThickness = value;
				Settings.Default.Save();
				OnPropertyChanged();
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
				if (Settings.Default.AutoOpenOrbitViewOnStartup == value) return;
				Settings.Default.AutoOpenOrbitViewOnStartup = value;
				Settings.Default.Save();
				OnPropertyChanged();
			}
		}

		#endregion

		#region INotifyPropertyChanged

		public event PropertyChangedEventHandler PropertyChanged;

		protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
			=> PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

		#endregion
	}
}
