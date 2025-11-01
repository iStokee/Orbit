using System;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using MahApps.Metro.IconPacks;
using Orbit.Logging;
using Orbit.Services;
using Orbit.Services.Updates;
using Orbit.Utilities;

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

			// Commands
			CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync());
			InstallUpdateCommand = new RelayCommand(async _ => await InstallUpdateAsync(), _ => CanInstallUpdate);

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
