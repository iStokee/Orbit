using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Plugins;
using Orbit.Services;
using Orbit.Services.Updates;
using Orbit.Tooling;
using Xunit;

namespace Orbit.Tests;

public sealed class PluginAndReleaseAuditTests
{
	[Fact]
	public async Task Loader_RejectsDuplicateKeyAndBlocksUnloadWhileViewsAreOpen()
	{
		var loader = new PluginLoader();
		var path = typeof(LoaderFixturePlugin).Assembly.Location;
		var first = await loader.LoadPluginAsync(path);
		Assert.True(first.Success, first.ErrorMessage);
		var folder = Path.Combine(Path.GetTempPath(), "OrbitPluginAudit-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(folder);
		var secondPath = Path.Combine(folder, "second.dll");
		File.Copy(path, secondPath);
		var second = await loader.LoadPluginAsync(secondPath);
		Assert.False(second.Success);
		Assert.Single(loader.LoadedPlugins);
		loader.IsPluginInUse = _ => true;
		Assert.False(await loader.UnloadPluginAsync(path));
		Assert.Single(loader.LoadedPlugins);
		loader.IsPluginInUse = _ => false;
		Assert.True(await loader.UnloadPluginAsync(path));
		Assert.Empty(loader.LoadedPlugins);
	}

	[Fact]
	public void Registry_RejectsCollisionWithoutReplacingExistingTool()
	{
		var registry = new ToolRegistry(Array.Empty<IOrbitTool>());
		var first = new LoaderFixturePlugin();
		registry.RegisterPluginTool(first);
		Assert.Throws<InvalidOperationException>(() => registry.RegisterPluginTool(new LoaderFixturePlugin()));
		Assert.Same(first, registry.Find(first.Key));
	}

	[Theory]
	[InlineData("Microsoft.Data.Sqlite", false)]
	[InlineData("Microsoft.Extensions.CustomPlugin", false)]
	[InlineData("Microsoft.CSharp", true)]
	[InlineData("Orbit.Plugin.Abstractions", true)]
	public void PluginDependencies_OnlyShareKnownHostAssemblies(string name, bool shared)
		=> Assert.Equal(shared, PluginLoadContext.ShouldUseDefaultContext(name));

	[Fact]
	public void ReleaseSelection_SkipsIncompleteAndForeignEntries()
	{
		var invalid = new GitHubReleaseChecker.GitHubRelease { tag_name = "v9.0.0", assets = Array.Empty<GitHubReleaseChecker.GitHubAsset>() };
		var valid = new GitHubReleaseChecker.GitHubRelease
		{
			tag_name = "orbit/v1.0.10", author = new() { login = "iStokee" },
			assets = new[] { new GitHubReleaseChecker.GitHubAsset { name = "orbit-win-x64.zip" }, new GitHubReleaseChecker.GitHubAsset { name = "orbit-win-x64.zip.sha256" } }
		};
		Assert.Same(valid, GitHubReleaseChecker.SelectRelease(new[] { invalid, valid }, "orbit-win-x64.zip", "iStokee", false));
	}

	[Fact]
	public void Preferences_StoreInstancesShareSavedStateWithoutSharingMutableObjects()
	{
		var path = Path.Combine(Path.GetTempPath(), "OrbitPrefsAudit-" + Guid.NewGuid().ToString("N"), "settings.json");
		var first = new McpPreferencesStore(path);
		var second = new McpPreferencesStore(path);
		var preferences = first.Load();
		preferences.AutoProbeSeconds = 11;
		Assert.Equal(3, second.Load().AutoProbeSeconds);
		first.Save(preferences);
		Assert.Equal(11, second.Load().AutoProbeSeconds);
		preferences.AutoProbeSeconds = 19;
		Assert.Equal(11, first.Load().AutoProbeSeconds);
	}
}

// Loaded from this test assembly through a real collectible plugin context.
public sealed class LoaderFixturePlugin : IOrbitPlugin
{
	public string Key => "OrbitAuditFixture";
	public string DisplayName => "Audit fixture";
	public string Version => "1.0.0";
	public string Author => "test";
	public string Description => "test";
	public PackIconMaterialKind Icon => PackIconMaterialKind.Tools;
	public void OnLoad()
	{
		if (Environment.GetEnvironmentVariable("ORBIT_TEST_FAIL_ONLOAD") == "1")
		{
			Environment.SetEnvironmentVariable("ORBIT_TEST_FAIL_ONLOAD", null);
			throw new InvalidOperationException("Candidate initialization failed.");
		}
	}
	public void OnUnload() { }
	public FrameworkElement CreateView(object? context = null) => throw new NotSupportedException();
}
