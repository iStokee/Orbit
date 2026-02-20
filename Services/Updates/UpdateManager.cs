using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Orbit.Services.Updates
{
	/// <summary>
	/// Manages downloading and applying updates
	/// </summary>
	public sealed class UpdateManager
	{
		private static readonly HttpClient _http = new HttpClient();

		/// <summary>
		/// Gets the folder where updates are downloaded
		/// </summary>
		public static string GetUpdateFolder()
		{
			var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Orbit", "updates");
			Directory.CreateDirectory(root);
			return root;
		}

		/// <summary>
		/// Downloads an update from the specified URL
		/// </summary>
		/// <param name="downloadUrl">GitHub asset download URL</param>
		/// <param name="assetName">Name of the asset file</param>
		/// <param name="progress">Optional progress callback (0-100)</param>
		/// <returns>Path to downloaded file</returns>
		public async Task<string> DownloadUpdateAsync(
			string downloadUrl,
			string assetName,
			IProgress<int> progress = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(downloadUrl))
				throw new ArgumentException("downloadUrl is missing");
			if (string.IsNullOrWhiteSpace(assetName))
				throw new ArgumentException("assetName is missing");

			var folder = GetUpdateFolder();
			var safeAssetName = Path.GetFileName(assetName);
			if (!string.Equals(safeAssetName, assetName, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(safeAssetName))
				throw new ArgumentException("assetName must be a file name only", nameof(assetName));

			var targetFile = Path.Combine(folder, safeAssetName);
			var tempFile = $"{targetFile}.download";

			using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
			resp.EnsureSuccessStatusCode();

			var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
			var canReportProgress = totalBytes != -1L && progress != null;

			try
			{
				await using (var fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
				{
					await using (var stream = await resp.Content.ReadAsStreamAsync(cancellationToken))
					{
						var buffer = new byte[8192];
						var totalRead = 0L;
						int bytesRead;

						while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
						{
							await fs.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
							totalRead += bytesRead;

							if (canReportProgress)
							{
								var progressPercentage = (int)((totalRead * 100) / totalBytes);
								progress.Report(progressPercentage);
							}
						}
					}
				}

				// Avoid leaving a partially downloaded asset as the primary file when a download is interrupted.
				File.Move(tempFile, targetFile, overwrite: true);
			}
			catch
			{
				try
				{
					if (File.Exists(tempFile))
					{
						File.Delete(tempFile);
					}
				}
				catch
				{
					// best effort temp cleanup
				}

				throw;
			}

			return targetFile;
		}

		/// <summary>
		/// Extracts a downloaded update zip file
		/// </summary>
		/// <param name="zipPath">Path to the downloaded zip</param>
		/// <returns>Path to extracted folder</returns>
		public string ExtractUpdate(string zipPath)
		{
			if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
				throw new FileNotFoundException("Downloaded update archive was not found", zipPath);

			var extractDir = Path.Combine(GetUpdateFolder(), "extracted");
			if (Directory.Exists(extractDir))
				Directory.Delete(extractDir, true);

			Directory.CreateDirectory(extractDir);

			ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);
			return extractDir;
		}

		/// <summary>
		/// Launches the updater tool and exits Orbit
		/// </summary>
		/// <param name="extractedFolder">Path to extracted update files</param>
		public void LaunchUpdaterAndExit(string extractedFolder)
		{
			if (string.IsNullOrWhiteSpace(extractedFolder) || !Directory.Exists(extractedFolder))
			{
				throw new DirectoryNotFoundException($"Extracted update folder not found: {extractedFolder}");
			}

			// assumes you ship Orbit.Updater.exe next to Orbit.exe
			var currentDir = AppContext.BaseDirectory;
			var updaterPath = Path.Combine(currentDir, "Orbit.Updater.exe");

			if (!File.Exists(updaterPath))
			{
				throw new FileNotFoundException("Updater not found. Please reinstall Orbit.", updaterPath);
			}

			// args: <sourceFolderWithNewFiles> <targetAppFolder> <exeToRestart>
			var targetAppFolder = currentDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			var exeToRestart = Path.Combine(targetAppFolder, "Orbit.exe");

			var psi = new ProcessStartInfo
			{
				FileName = updaterPath,
				Arguments = $"\"{extractedFolder}\" \"{targetAppFolder}\" \"{exeToRestart}\"",
				UseShellExecute = false,
				WorkingDirectory = targetAppFolder
			};

			var process = Process.Start(psi);
			if (process == null)
			{
				throw new InvalidOperationException("Failed to launch updater process.");
			}

			// now exit current app
			System.Windows.Application.Current.Shutdown();
		}

		/// <summary>
		/// Cleans up old update downloads
		/// </summary>
		public void CleanupOldUpdates()
		{
			try
			{
				var updateFolder = GetUpdateFolder();
				if (Directory.Exists(updateFolder))
				{
					Directory.Delete(updateFolder, true);
				}
			}
			catch
			{
				// best effort cleanup
			}
		}
	}
}
