using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
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
		public async Task<string> DownloadUpdateAsync(string downloadUrl, string assetName, IProgress<int> progress = null)
		{
			if (string.IsNullOrWhiteSpace(downloadUrl))
				throw new ArgumentException("downloadUrl is missing");

			var folder = GetUpdateFolder();
			var targetFile = Path.Combine(folder, assetName);

			using var resp = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
			resp.EnsureSuccessStatusCode();

			var totalBytes = resp.Content.Headers.ContentLength ?? -1L;
			var canReportProgress = totalBytes != -1L && progress != null;

			await using (var fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None))
			{
				await using (var stream = await resp.Content.ReadAsStreamAsync())
				{
					var buffer = new byte[8192];
					var totalRead = 0L;
					int bytesRead;

					while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
					{
						await fs.WriteAsync(buffer, 0, bytesRead);
						totalRead += bytesRead;

						if (canReportProgress)
						{
							var progressPercentage = (int)((totalRead * 100) / totalBytes);
							progress.Report(progressPercentage);
						}
					}
				}
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
			var extractDir = Path.Combine(GetUpdateFolder(), "extracted");
			if (Directory.Exists(extractDir))
				Directory.Delete(extractDir, true);

			Directory.CreateDirectory(extractDir);

			ZipFile.ExtractToDirectory(zipPath, extractDir);
			return extractDir;
		}

		/// <summary>
		/// Launches the updater tool and exits Orbit
		/// </summary>
		/// <param name="extractedFolder">Path to extracted update files</param>
		public void LaunchUpdaterAndExit(string extractedFolder)
		{
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
				UseShellExecute = false
			};

			Process.Start(psi);

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
