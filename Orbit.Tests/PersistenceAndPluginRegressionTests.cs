using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Orbit.Models;
using Orbit.Plugins;
using Orbit.Services;
using Xunit;

namespace Orbit.Tests;

public sealed class PersistenceAndPluginRegressionTests
{
	[Theory]
	[InlineData("")]
	[InlineData("{")]
	[InlineData("{}")]
	public void LauncherAccounts_LoadFailureCannotOverwriteOriginal(string damaged)
	{
		var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "accounts.json");
		File.WriteAllText(path, damaged);
		Assert.ThrowsAny<Exception>(() => LauncherAccountStore.Load(path));
		Assert.ThrowsAny<Exception>(() => LauncherAccountStore.Save(Array.Empty<LauncherAccountModel>(), path));
		Assert.Equal(damaged, File.ReadAllText(path));
		Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
	}

	[Fact]
	public void LauncherAccounts_AtomicReplacementPreservesOriginalOnWriteFailure()
	{
		var folder = ClientLifecycleRegressionTests.CreateFolder();
		var path = Path.Combine(folder, "accounts.json");
		File.WriteAllText(path, "[]");
		using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
			Assert.ThrowsAny<IOException>(() => LauncherAccountStore.Save(new[] { new LauncherAccountModel { DisplayName = "new" } }, path));
		Assert.Equal("[]", File.ReadAllText(path));
		Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
		LauncherAccountStore.Save(new[] { new LauncherAccountModel { DisplayName = "new", IsSelected = true } }, path);
		Assert.True(Assert.Single(LauncherAccountStore.Load(path)).IsSelected);
	}

	[Fact]
	public void ScriptHistory_EvictionPreservesFavoriteAndConfigurationAcrossRestart()
	{
		var json = "[]";
		var service = new ScriptManagerService(() => json, value => json = value);
		service.AddOrUpdateScript("C:\\custom\\favorite.dll", "Favorite", "configured", "my.custom.id");
		var favorite = service.FindByPath("C:\\custom\\favorite.dll")!;
		service.ToggleFavorite(favorite);
		for (var i = 0; i < 15; i++) service.AddOrUpdateScript($"C:\\scripts\\script{i}.dll");
		Assert.Equal(10, service.RecentScripts.Count());
		Assert.DoesNotContain(favorite, service.RecentScripts);
		Assert.Same(favorite, Assert.Single(service.GetFavorites()));
		var restored = new ScriptManagerService(() => json, value => json = value);
		var configuration = Assert.Single(restored.GetFavorites());
		Assert.Equal("my.custom.id", configuration.ScriptId);
		Assert.Equal("configured", configuration.Description);
		restored.AddOrUpdateScript(configuration.FilePath);
		Assert.Equal("my.custom.id", configuration.ScriptId);
		Assert.Contains(configuration, restored.RecentScripts);
		Assert.Equal(10, restored.RecentScripts.Count());
	}

	[Fact]
	public async Task PluginReload_DetectsDependencyOnlyChangeAndShutdownRejectsNewLoads()
	{
		var folder = ClientLifecycleRegressionTests.CreateFolder();
		var path = Path.Combine(folder, "fixture.dll");
		File.Copy(typeof(LoaderFixturePlugin).Assembly.Location, path);
		var dependency = Path.Combine(folder, "dependency.json");
		File.WriteAllText(dependency, "{\"version\":1}");
		var loader = new PluginLoader();
		var first = await loader.LoadPluginAsync(path);
		Assert.True(first.Success, first.ErrorMessage);
		try
		{
			File.WriteAllText(dependency, "{\"version\":2}");
			var reloaded = await loader.LoadPluginAsync(path);
			Assert.True(reloaded.Success, reloaded.ErrorMessage);
			Assert.True(reloaded.WasReloaded);
			Assert.NotEqual(first.Metadata!.FileHash, reloaded.Metadata!.FileHash);
			loader.IsPluginInUse = _ => true;
			await loader.ShutdownAsync();
			Assert.Empty(loader.LoadedPlugins);
			Assert.False((await loader.LoadPluginAsync(path)).Success);
		}
		finally { await loader.ShutdownAsync(); }
	}
}
