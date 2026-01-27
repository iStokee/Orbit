using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Orbit;

namespace Orbit.Services.Updates
{
	/// <summary>
	/// Checks GitHub releases for updates to Orbit
	/// </summary>
	public sealed class GitHubReleaseChecker
	{
		private static readonly HttpClient _http = new HttpClient();

		public const string DefaultAssetName = UpdateConfig.DefaultAssetName;

		public sealed class GitHubRelease
		{
			public string tag_name { get; set; }
			public GitHubAsset[] assets { get; set; }
			public GitHubUser author { get; set; }
			public bool prerelease { get; set; }
		}

		public sealed class GitHubAsset
		{
			public string name { get; set; }
			public string browser_download_url { get; set; }
		}

		public sealed class GitHubUser
		{
			public string login { get; set; }
		}

		public sealed class UpdateInfo
		{
			public bool HasUpdate { get; set; }
			public Version CurrentVersion { get; set; }
			public Version RemoteVersion { get; set; }
			public string DownloadUrl { get; set; }
			public string AssetName { get; set; }
			public string ReleaseAuthor { get; set; }
			public string ErrorMessage { get; set; }
		}

		/// <summary>
		/// Checks for updates from GitHub releases
		/// </summary>
		/// <param name="expectedAssetName">The asset name to look for (e.g., "orbit-win-x64.zip")</param>
		/// <param name="expectedAuthor">Optional - only accept releases from this GitHub user</param>
		/// <param name="includePrereleases">Whether to include pre-release versions</param>
		public async Task<UpdateInfo> CheckAsync(string expectedAssetName = DefaultAssetName,
												 string expectedAuthor = null,
												 bool includePrereleases = false)
		{
			var currentVersion = GetCurrentVersion();

			try
			{
				var url = $"https://api.github.com/repos/{UpdateConfig.Owner}/{UpdateConfig.Repo}/releases/latest";
				var req = new HttpRequestMessage(HttpMethod.Get, url);
				// GitHub requires a user-agent
				req.Headers.UserAgent.ParseAdd("Orbit-Updater/1.0");

				var resp = await _http.SendAsync(req);
				resp.EnsureSuccessStatusCode();

				var json = await resp.Content.ReadAsStringAsync();
				var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
				{
					PropertyNameCaseInsensitive = true
				});

				if (release == null)
				{
					return new UpdateInfo
					{
						HasUpdate = false,
						CurrentVersion = currentVersion,
						RemoteVersion = currentVersion,
						ErrorMessage = "Failed to parse release information"
					};
				}

				if (release.prerelease && !includePrereleases)
				{
					// ignore prereleases
					return new UpdateInfo
					{
						HasUpdate = false,
						CurrentVersion = currentVersion,
						RemoteVersion = currentVersion
					};
				}

				// Optional: make sure only the expected author can publish something Orbit accepts
				if (!string.IsNullOrWhiteSpace(expectedAuthor) &&
					!string.Equals(release.author?.login, expectedAuthor, StringComparison.OrdinalIgnoreCase))
				{
					return new UpdateInfo
					{
						HasUpdate = false,
						CurrentVersion = currentVersion,
						RemoteVersion = currentVersion,
						ErrorMessage = $"Release author '{release.author?.login}' does not match expected '{expectedAuthor}'"
					};
				}

				// tag_name -> "v1.2.3"
				var remoteVersionString = release.tag_name?.TrimStart('v', 'V');
				if (!TryParseVersion(remoteVersionString, out var remoteVersion))
				{
					// bad tag? just bail
					return new UpdateInfo
					{
						HasUpdate = false,
						CurrentVersion = currentVersion,
						RemoteVersion = currentVersion,
						ErrorMessage = $"Invalid version tag: {release.tag_name}"
					};
				}

				// find our asset
				GitHubAsset asset = null;
				if (release.assets != null)
				{
					foreach (var a in release.assets)
					{
						if (string.Equals(a.name, expectedAssetName, StringComparison.OrdinalIgnoreCase))
						{
							asset = a;
							break;
						}
					}
				}
				else
				{
					return new UpdateInfo
					{
						HasUpdate = false,
						CurrentVersion = currentVersion,
						RemoteVersion = remoteVersion,
						ReleaseAuthor = release.author?.login,
						ErrorMessage = "Release contains no assets"
					};
				}

				var hasUpdate = remoteVersion > currentVersion;

				if (asset == null)
				{
					return new UpdateInfo
					{
						HasUpdate = false,
						CurrentVersion = currentVersion,
						RemoteVersion = remoteVersion,
						ReleaseAuthor = release.author?.login,
						ErrorMessage = $"Asset '{expectedAssetName}' not found in release"
					};
				}

				return new UpdateInfo
				{
					HasUpdate = hasUpdate,
					CurrentVersion = currentVersion,
					RemoteVersion = remoteVersion,
					DownloadUrl = asset.browser_download_url,
					AssetName = asset.name,
					ReleaseAuthor = release.author?.login
				};
			}

			catch (Exception ex)
			{
				return new UpdateInfo
				{
					HasUpdate = false,
					CurrentVersion = currentVersion,
					RemoteVersion = currentVersion,
					ErrorMessage = $"Failed to check for updates: {ex.Message}"
				};
			}
		}

		private static Version GetCurrentVersion()
		{
			return AppVersion.Parsed;
		}

		private static bool TryParseVersion(string input, out Version version)
		{
			version = null;
			if (string.IsNullOrWhiteSpace(input))
				return false;

			var match = Regex.Match(input, @"\d+(\.\d+){1,3}");
			if (!match.Success)
				return false;

			return Version.TryParse(match.Value, out version);
		}
	}
}
