using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
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
		/// Downloads an update from the specified URL and verifies it against the
		/// release's published SHA-256 checksum before making it available.
		/// </summary>
		/// <param name="downloadUrl">GitHub asset download URL</param>
		/// <param name="assetName">Name of the asset file</param>
		/// <param name="sha256Url">GitHub download URL of the release's <c>&lt;asset&gt;.sha256</c> checksum asset</param>
		/// <param name="progress">Optional progress callback (0-100)</param>
		/// <returns>Path to downloaded file</returns>
		public async Task<string> DownloadUpdateAsync(
			string downloadUrl,
			string assetName,
			string sha256Url,
			IProgress<int> progress = null,
			CancellationToken cancellationToken = default)
		{
			if (string.IsNullOrWhiteSpace(downloadUrl))
				throw new ArgumentException("downloadUrl is missing");
			if (string.IsNullOrWhiteSpace(assetName))
				throw new ArgumentException("assetName is missing");
			if (string.IsNullOrWhiteSpace(sha256Url))
				throw new InvalidDataException(
					$"Release does not include the '{assetName}.sha256' checksum asset; refusing to install an unverifiable update.");

			ValidateAssetDownloadUrl(downloadUrl);
			ValidateAssetDownloadUrl(sha256Url);

			var sha256Content = await _http.GetStringAsync(sha256Url, cancellationToken);
			var expectedSha256 = ParseSha256(sha256Content);

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

				var actualSha256 = await ComputeFileSha256Async(tempFile, cancellationToken);
				if (!string.Equals(actualSha256, expectedSha256, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException(
						$"Downloaded update failed SHA-256 verification (expected {expectedSha256}, got {actualSha256}). The download may be corrupt or tampered with.");
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
		/// Rejects download URLs that don't point at this project's GitHub release assets.
		/// The update path ends in "replace and relaunch Orbit.exe", so the source host is
		/// pinned rather than trusting whatever URL the release JSON hands back.
		/// </summary>
		public static void ValidateAssetDownloadUrl(string url)
		{
			if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
				throw new InvalidDataException($"Update download URL is not a valid absolute URL: {url}");

			if (uri.Scheme != Uri.UriSchemeHttps)
				throw new InvalidDataException($"Update download URL must use https: {url}");

			if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"Update download URL host '{uri.Host}' is not github.com: {url}");

			var expectedPrefix = $"/{UpdateConfig.Owner}/{UpdateConfig.Repo}/releases/download/";
			if (!uri.AbsolutePath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException(
					$"Update download URL does not point at {UpdateConfig.Owner}/{UpdateConfig.Repo} release assets: {url}");
		}

		/// <summary>
		/// Parses the hash out of a .sha256 checksum file (sha256sum format: hex digest, optionally followed by a file name).
		/// </summary>
		public static string ParseSha256(string content)
		{
			var token = (content ?? string.Empty)
				.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
				.FirstOrDefault();

			if (token == null || token.Length != 64 || !token.All(Uri.IsHexDigit))
				throw new InvalidDataException("Release checksum asset does not contain a valid SHA-256 digest.");

			return token;
		}

		private static async Task<string> ComputeFileSha256Async(string path, CancellationToken cancellationToken)
		{
			await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			using var sha = SHA256.Create();
			var hash = await sha.ComputeHashAsync(stream, cancellationToken);
			return Convert.ToHexString(hash);
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

			using var archive = ZipFile.OpenRead(zipPath);
			ValidateArchiveEntries(archive, extractDir);
			archive.ExtractToDirectory(extractDir, overwriteFiles: true);
			return extractDir;
		}

		private static void ValidateArchiveEntries(ZipArchive archive, string extractDir)
		{
			var root = Path.GetFullPath(extractDir);
			if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
			{
				root += Path.DirectorySeparatorChar;
			}

			foreach (var entry in archive.Entries)
			{
				if (string.IsNullOrWhiteSpace(entry.FullName))
				{
					throw new InvalidDataException("Update archive contains an empty entry name.");
				}

				var destinationPath = Path.GetFullPath(Path.Combine(root, entry.FullName));
				if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException($"Update archive contains an unsafe path: {entry.FullName}");
				}
			}
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
