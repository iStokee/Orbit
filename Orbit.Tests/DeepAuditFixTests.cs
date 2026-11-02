using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Logging;
using Orbit.Models;
using Orbit.Plugins;
using Orbit.Services;
using Orbit.Services.Updates;
using Orbit.ViewModels;
using Xunit;

namespace Orbit.Tests;

public sealed class DeepAuditFixTests
{
	[Theory]
	[InlineData("{")]
	[InlineData("null")]
	[InlineData("[]")]
	public void InjectorSettings_CorruptionCannotBeOverwritten(string damaged)
	{
		var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "settings.json");
		File.WriteAllText(path, damaged);
		var service = new McpInjectorSettingsService(path);
		var settings = service.Load();
		Assert.NotNull(settings.Error);
		Assert.ThrowsAny<Exception>(() => service.Save(settings with { AutoStart = false }));
		Assert.Equal(damaged, File.ReadAllText(path));
	}

	[Fact]
	public void InjectorSettings_PreservesOtherFieldsAndReplacesAtomically()
	{
		var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "settings.json");
		File.WriteAllText(path, "{\"other\":42}");
		var service = new McpInjectorSettingsService(path);
		service.Save(service.Load() with { AutoStart = false });
		Assert.Contains("42", File.ReadAllText(path));
		Assert.False(service.Load().AutoStart);
		Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
	}

	[Theory]
	[InlineData("{")]
	[InlineData("null")]
	public void ScriptCatalog_CorruptionIsVisibleAndCannotBeOverwritten(string damaged)
	{
		var writes = 0;
		var service = new ScriptManagerService(() => damaged, _ => writes++);
		service.AddOrUpdateScript("C:\\scripts\\a.dll");
		Assert.NotNull(service.LastError);
		Assert.Equal(0, writes);
	}

	[Fact]
	public void ScriptCatalog_ReadAndWriteFailuresAreVisible()
	{
		var readFailed = new ScriptManagerService(() => throw new IOException("read failed"), _ => { });
		Assert.Contains("read failed", readFailed.LastError);
		var saveFailed = new ScriptManagerService(() => "[]", _ => throw new IOException("disk full"));
		saveFailed.AddOrUpdateScript("C:\\scripts\\a.dll");
		Assert.Contains("disk full", saveFailed.LastError);
	}

	[Fact]
	public void RemovingFavoriteFromHistoryPreservesItsConfigurationAfterRestart()
	{
		var json = "[]";
		var service = new ScriptManagerService(() => json, value => json = value);
		service.AddOrUpdateScript("C:\\scripts\\a.dll", "Custom", "description", "custom.id");
		var profile = Assert.Single(service.Profiles);
		service.ToggleFavorite(profile);
		service.RemoveScript(profile);
		var restarted = new ScriptManagerService(() => json, value => json = value);
		Assert.Empty(restarted.RecentScripts);
		Assert.Equal("custom.id", Assert.Single(restarted.GetFavorites()).ScriptId);
	}

	[Fact]
	public async Task UpdateHandoff_DoesNotTearDownApplicationIfHelperFails()
	{
		var cleanup = false;
		var committed = false;
		await Assert.ThrowsAsync<IOException>(() => UpdateManager.CompleteHandoffAsync(
			() => Task.FromException(new IOException("helper failed")),
			() => { cleanup = true; return Task.CompletedTask; }, () => committed = true));
		Assert.False(cleanup);
		Assert.False(committed);
	}

	[Fact]
	public async Task UpdateHandoff_WaitsForCleanupBeforeReleasingHelper()
	{
		var cleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var committed = false;
		var operation = UpdateManager.CompleteHandoffAsync(() => Task.CompletedTask, () => cleanup.Task, () => committed = true);
		Assert.False(committed);
		cleanup.SetResult();
		await operation;
		Assert.True(committed);
	}

	[Fact]
	public async Task RemoteConsole_RejectsUnterminatedOversizeLines()
	{
		var reader = new BoundedLineReader(new StringReader(new string('x', BoundedLineReader.MaxLineLength + 1)));
		await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadLineAsync(CancellationToken.None));
		var valid = new BoundedLineReader(new StringReader("one\r\ntwo\nlast"));
		Assert.Equal("one", await valid.ReadLineAsync(CancellationToken.None));
		Assert.Equal("two", await valid.ReadLineAsync(CancellationToken.None));
		Assert.Equal("last", await valid.ReadLineAsync(CancellationToken.None));
		Assert.Null(await valid.ReadLineAsync(CancellationToken.None));
	}

	[Fact]
	public void ToolClose_DisposesOwnedViewExactlyOnceAndRemovesItFromRegistry()
		=> ClientLifecycleRegressionTests.RunOnSta(() =>
		{
			var view = new OwnedTestView();
			var tool = new ToolTabItem("test-owned", "Test", view);
			Assert.Contains(tool, ToolInstanceRegistry.Instance.OpenTools);
			tool.Close(); tool.Close();
			Assert.Equal(1, view.DisposeCount);
			Assert.DoesNotContain(tool, ToolInstanceRegistry.Instance.OpenTools);
		});

	[Fact]
	public void UiFactory_DoesNotLeaveDisposableModelsOwnedByRootProvider()
	{
		var provider = new ServiceCollection().BuildServiceProvider();
		var model = new OwnedViewFactory(provider).CreateModel<DisposableModel>();
		model.Dispose();
		provider.Dispose();
		Assert.Equal(1, model.DisposeCount);
	}

	[Theory]
	[InlineData(MessageBoxResult.Yes, "OrbitView")]
	[InlineData(MessageBoxResult.No, "IndividualTabs")]
	[InlineData(MessageBoxResult.Cancel, null)]
	[InlineData(MessageBoxResult.None, null)]
	public void AskPlacement_CancelNeverDefaultsToLaunching(MessageBoxResult answer, string? expected)
		=> Assert.Equal(expected, SessionStartupService.ResolvePlacementChoice(answer));

	[Fact]
	public void MovingToolPreservesItsOwnedViewModel()
		=> ClientLifecycleRegressionTests.RunOnSta(() =>
		{
			var collection = SessionCollectionService.Instance;
			var placement = new SessionPlacementService();
			var reconciliation = new SessionReconciliationService(collection, placement, new TearOffHostRegistry());
			var layout = new OrbitLayoutStateService(collection, placement, reconciliation);
			var coordinator = new SessionUiCoordinatorService(placement, reconciliation, layout);
			var model = new object();
			var tool = new ToolTabItem("owned-context", "Test", new Border { DataContext = model });
			try
			{
				Assert.True(coordinator.MoveItemToOrbit(tool, toolDataContext: new object()));
				Assert.Same(model, tool.HostControl.DataContext);
			}
			finally { layout.RemoveItem(tool); tool.Close(); }
		});

	[Fact]
	public void NativeRestore_ReportsFailureAndRecoversFromDestroyedOriginalParent()
		=> ClientLifecycleRegressionTests.RunOnSta(() =>
		{
			Assert.Throws<System.ComponentModel.Win32Exception>(() => NativeWindowRestorer.Restore(0, 0, 0));
			var oldParent = CreateWindowEx(0, "STATIC", "parent", 0, 0, 0, 10, 10, 0, 0, 0, 0);
			var host = CreateWindowEx(0, "STATIC", "host", 0, 0, 0, 10, 10, 0, 0, 0, 0);
			var child = CreateWindowEx(0, "STATIC", "child", 0x40000000, 0, 0, 10, 10, host, 0, 0, 0);
			Assert.NotEqual(0, child);
			DestroyWindow(oldParent);
			try
			{
				NativeWindowRestorer.Restore(child, oldParent, 0x40000000);
				Assert.Equal(0, GetParent(child));
				Assert.Equal(0, GetWindowLong(child, -16) & 0x40000000);
			}
			finally { DestroyWindow(child); DestroyWindow(host); }
		});

	[DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateWindowEx(uint extended, string className, string name, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
	[DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
	[DllImport("user32.dll")] private static extern nint GetParent(nint window);
	[DllImport("user32.dll")] private static extern int GetWindowLong(nint window, int index);

	[Fact]
	public void ExpiredUiRequestsCannotStartWithoutCancellation()
	{
		var called = false;
		Assert.Throws<TimeoutException>(() => { _ = UiRequestDispatcher.RunAsync(null, () => called = true,
			CancellationToken.None, DateTimeOffset.UtcNow.AddSeconds(-1)); });
		Assert.False(called);
	}

	[Fact]
	public async Task PluginPackage_IgnoresLogsAndStagesImmutableDependencies()
	{
		var folder = ClientLifecycleRegressionTests.CreateFolder();
		var entry = Path.Combine(folder, "fixture.dll");
		File.Copy(typeof(LoaderFixturePlugin).Assembly.Location, entry);
		var dependency = Path.Combine(folder, "dependency.json");
		File.WriteAllText(dependency, "old");
		var hash = await PluginPackage.HashAsync(entry);
		Directory.CreateDirectory(Path.Combine(folder, "logs"));
		File.WriteAllText(Path.Combine(folder, "logs", "mutable.json"), "log");
		Assert.Equal(hash, await PluginPackage.HashAsync(entry));
		using var stage = PluginPackage.Stage(entry);
		File.WriteAllText(dependency, "new");
		Assert.Equal("old", File.ReadAllText(Path.Combine(stage.Folder, "dependency.json")));
		Assert.NotEqual(hash, await PluginPackage.HashAsync(entry));
		Assert.Equal(hash, await PluginPackage.HashAsync(Path.Combine(stage.Folder, stage.EntryPath)));
	}

	[Fact]
	public void PluginDiscovery_InspectsEntryMetadataInsteadOfFileNameOrLoadingDependencies()
	{
		var folder = ClientLifecycleRegressionTests.CreateFolder();
		var plugin = Path.Combine(folder, "Microsoft.CustomPlugin.dll");
		File.Copy(typeof(LoaderFixturePlugin).Assembly.Location, plugin);
		File.Copy(typeof(System.Text.Json.JsonDocument).Assembly.Location, Path.Combine(folder, "helper.dll"));
		var loader = new PluginLoader();
		Assert.Equal(plugin, Assert.Single(loader.DiscoverPlugins(folder)));
		Assert.Empty(loader.LoadedPlugins);
		File.WriteAllText(Path.Combine(folder, "orbit.plugin.json"), "{");
		Assert.Empty(loader.DiscoverPlugins(folder));
	}

	[Fact]
	public async Task PluginReplacement_InitializationFailureRestoresOriginalInstance()
	{
		var folder = ClientLifecycleRegressionTests.CreateFolder();
		var entry = Path.Combine(folder, "fixture.dll");
		File.Copy(typeof(LoaderFixturePlugin).Assembly.Location, entry);
		var loader = new PluginLoader();
		Assert.True((await loader.LoadPluginAsync(entry)).Success);
		var original = loader.GetPlugin("OrbitAuditFixture");
		try
		{
			File.WriteAllText(Path.Combine(folder, "dependency.json"), "changed");
			Environment.SetEnvironmentVariable("ORBIT_TEST_FAIL_ONLOAD", "1");
			Assert.False((await loader.LoadPluginAsync(entry)).Success);
			Assert.Same(original, loader.GetPlugin("OrbitAuditFixture"));
			Assert.True(Assert.Single(loader.LoadedPlugins).IsLoaded);
		}
		finally { Environment.SetEnvironmentVariable("ORBIT_TEST_FAIL_ONLOAD", null); await loader.ShutdownAsync(); }
	}

	[Fact]
	public async Task GuideNavigation_StaleReadCannotReplaceNewSelection()
		=> await RunOnStaAsync(async () =>
		{
			var folder = ClientLifecycleRegressionTests.CreateFolder();
			File.WriteAllText(Path.Combine(folder, "README.md"), "");
			File.WriteAllText(Path.Combine(folder, "UserGuide.md"), "");
			var reads = new Dictionary<string, TaskCompletionSource<string>>();
			using var vm = new GuideBrowserViewModel(folder, (path, _) =>
			{
				var completion = new TaskCompletionSource<string>(); reads[Path.GetFileName(path)] = completion; return completion.Task;
			});
			vm.SelectedSection = vm.Sections[1];
			reads["UserGuide.md"].SetResult("# New document");
			await Task.Yield();
			var document = vm.CurrentDocument;
			Assert.NotNull(document);
			reads["README.md"].SetResult("# Stale document");
			await Task.Yield();
			Assert.Same(document, vm.CurrentDocument);
			Assert.False(vm.IsLoading);
		});

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task McpProbe_IgnoresResponseAfterSelectionChangeOrToolClose(bool closeTool)
		=> await RunOnStaAsync(async () =>
		{
			using var process = Process.GetCurrentProcess();
			var sessions = SessionCollectionService.Instance;
			var session = new SessionModel { Id = Guid.NewGuid(), Name = "Synthetic MCP", RSProcess = process };
			session.UpdateInjectionState(InjectionState.Injected);
			sessions.Sessions.Add(session);
			var folder = ClientLifecycleRegressionTests.CreateFolder();
			using var vm = new McpControlCenterViewModel(sessions, new McpBridgeClientService(),
				new McpPreferencesStore(Path.Combine(folder, "preferences.json")),
				new McpInjectorSettingsService(Path.Combine(folder, "injector.json")));
			using var pipe = new NamedPipeServerStream($"MESharpMcpBridge.{process.Id}", PipeDirection.InOut,
				1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
			try
			{
				var accepted = pipe.WaitForConnectionAsync();
				var probe = vm.ProbeStatusCommand.ExecuteAsync(null);
				await accepted.WaitAsync(TimeSpan.FromSeconds(3));
				using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
				Assert.Contains("me.get_status", await reader.ReadLineAsync());
				if (closeTool) vm.Dispose(); else vm.SelectedSession = null;
				if (!closeTool)
				{
					using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
					await writer.WriteLineAsync("{\"ok\":true,\"payload\":{\"stale\":true}}");
				}
				await probe.WaitAsync(TimeSpan.FromSeconds(3));
				Assert.Equal("{}", vm.LastResponseJson);
				Assert.False(vm.CanInteractWithSession);
			}
			finally { sessions.Sessions.Remove(session); }
		});

	[Fact]
	public void CustomThemes_CorruptionIsPreserved()
	{
		var original = Settings.Default.CustomThemes;
		try
		{
			Settings.Default.CustomThemes = "{";
			var service = new ThemeService();
			Assert.Empty(service.LoadCustomThemes());
			Assert.NotNull(service.LastError);
			service.SaveCustomThemes(Array.Empty<CustomThemeDefinition>());
			Assert.Equal("{", Settings.Default.CustomThemes);
		}
		finally { Settings.Default.CustomThemes = original; }
	}

	internal static async Task RunOnStaAsync(Func<Task> action)
	{
		var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var thread = new Thread(() =>
		{
			var dispatcher = Dispatcher.CurrentDispatcher;
			SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
			dispatcher.BeginInvoke(new Action(async () =>
			{
				try { await action(); completion.SetResult(); } catch (Exception ex) { completion.SetException(ex); }
				finally { dispatcher.InvokeShutdown(); }
			}));
			Dispatcher.Run();
		}) { IsBackground = true };
		thread.SetApartmentState(ApartmentState.STA); thread.Start();
		await completion.Task.WaitAsync(TimeSpan.FromSeconds(10));
	}

	private sealed class OwnedTestView : FrameworkElement, IToolViewLifetime
	{
		internal int DisposeCount;
		public void Dispose() => DisposeCount++;
	}
	public sealed class DisposableModel : IDisposable
	{
		public int DisposeCount;
		public void Dispose() => DisposeCount++;
	}
}
