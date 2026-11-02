using System.Collections.Specialized;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Reflection;
using System.Text;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Models;
using Orbit.Logging;
using Orbit.Plugins;
using Orbit.Services;
using Orbit.Tooling;
using Orbit.ViewModels;
using Xunit;

namespace Orbit.Tests
{
    public sealed class ToolDashboardRegressionTests
    {
        [Fact]
        public void AccountDelete_UsesRowParameterAndDoesNotRequireSelection()
        {
            var service = new AccountService(Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "accounts.json"));
            var first = new AccountModel { Username = "first", Password = "" };
            var second = new AccountModel { Username = "second", Password = "" };
            service.AddAccount(first); service.AddAccount(second);
            using var vm = new AccountManagerViewModel(service) { SelectedAccount = second };
            Assert.True(vm.DeleteAccountCommand.CanExecute(first));
            vm.DeleteAccountCommand.Execute(first);
            Assert.Same(second, Assert.Single(service.Accounts));
            Assert.Same(second, vm.SelectedAccount);
            vm.SelectedAccount = null;
            Assert.True(vm.DeleteAccountCommand.CanExecute(second));
            Assert.False(vm.DeleteAccountCommand.CanExecute(first));
        }

        [Fact]
        public void AccountNickname_DoesNotResetRowsOrWriteUntilCommitted()
        {
            var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "accounts.json");
            var service = new AccountService(path);
            var account = new AccountModel { Username = "first", Password = "" };
            service.AddAccount(account);
            using var vm = new AccountManagerViewModel(service);
            var original = File.ReadAllText(path);
            var resets = 0;
            vm.FilteredAccounts.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Reset) resets++; };
            account.Nickname = "Edited";
            Assert.Equal(0, resets);
            Assert.Equal(original, File.ReadAllText(path));
            vm.CommitAccountEdit();
            Assert.Contains("Edited", File.ReadAllText(path));
        }

        [Theory]
        [InlineData("{")]
        [InlineData("null")]
        [InlineData("[]")]
        public void McpPreferences_CorruptionIsPreservedAcrossStoreInstancesAndCanRecover(string damaged)
        {
            var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "mcp.json");
            File.WriteAllText(path, damaged);
            var store = new McpPreferencesStore(path);
            store.Load();
            Assert.NotNull(store.LastError);
            Assert.Throws<InvalidOperationException>(() => new McpPreferencesStore(path).Save(new()));
            Assert.Equal(damaged, File.ReadAllText(path));
            File.WriteAllText(path, "{\"AutoStartRuntimeOnInject\":false}");
            Assert.False(store.Load().AutoStartRuntimeOnInject);
            Assert.Null(store.LastError);
            store.Save(new McpPreferences { AutoStartRuntimeOnInject = false, AutoProbeSeconds = 7 });
            Assert.Equal(7, store.Load().AutoProbeSeconds);
        }

        [Fact]
        public void Discovery_IdentifiesEntryMetadataWithoutLoadingDependencies()
        {
            Assert.True(ScriptManagerService.HasScriptEntryPoint(typeof(MESharp.ScriptEntry).Assembly.Location));
            Assert.False(ScriptManagerService.HasScriptEntryPoint(typeof(ScriptManagerService).Assembly.Location));
            var invalid = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "native.dll");
            File.WriteAllText(invalid, "not a managed assembly");
            Assert.False(ScriptManagerService.HasScriptEntryPoint(invalid));
        }

        [Fact]
        public void McpPreferences_CachedSuccessCannotOverwriteSubsequentCorruption()
        {
            var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "mcp.json");
            var store = new McpPreferencesStore(path);
            store.Save(new McpPreferences { AutoProbeSeconds = 8 });
            Assert.Equal(8, store.Load().AutoProbeSeconds);
            File.WriteAllText(path, "{");
            Assert.Throws<InvalidOperationException>(() => store.Save(new()));
            Assert.Equal("{", File.ReadAllText(path));
            File.WriteAllText(path, "{\"AutoProbeSeconds\":9}");
            Assert.Equal(9, store.Load().AutoProbeSeconds);
            Assert.Null(store.LastError);
        }

        [Fact]
        public void Dashboard_RefreshRetainsFilterAndOperationOutcome()
        {
            var registry = new ToolRegistry(new IOrbitTool[] { new TestTool("Console"), new TestTool("Settings") });
            var sessions = SessionCollectionService.Instance;
            var placement = new SessionPlacementService();
            var reconciliation = new SessionReconciliationService(sessions, placement, new TearOffHostRegistry());
            var layout = new OrbitLayoutStateService(sessions, placement, reconciliation);
            var manager = new PluginManager(registry, ClientLifecycleRegressionTests.CreateFolder(), layout);
            using var vm = new UnifiedToolsManagerViewModel(registry, manager);
            vm.SearchFilter = "Console";
            vm.StatusMessage = "Failed to unload: close the view first.";
            vm.RefreshCommand.Execute(null);
            Assert.Equal("Console", Assert.Single(vm.ToolCards, c => c.IsVisible).Key);
            Assert.StartsWith("Failed to unload", vm.StatusMessage);
            Assert.Contains("2 built-in", vm.InventorySummary);
            Assert.True(Assert.Single(vm.ToolCards, c => c.Key == "Console").SupportsMenuVisibility);
            var arbitrary = new ToolCardViewModel(new TestTool("custom.tool"), true, null, manager, null);
            Assert.False(arbitrary.SupportsMenuVisibility);
            Assert.NotNull(arbitrary.OpenCommand);
        }

        [Fact]
        public void Gallery_DelegatesActivationToOwnerAwareReveal()
            => ClientLifecycleRegressionTests.RunOnSta(() =>
            {
                SessionModel? activated = null;
                var sessions = SessionCollectionService.Instance;
                var previous = sessions.GlobalSelectedSession;
                var session = new SessionModel { Id = Guid.NewGuid() };
                using var vm = new SessionGalleryViewModel(sessions, selected => activated = selected);
                try { vm.ActivateSessionCommand.Execute(session); Assert.Same(session, activated); }
                finally { sessions.GlobalSelectedSession = previous; }
            });

        [Fact]
        public void ScriptTarget_UnavailableOrRemovedSelectionCannotBecomeAnotherClient()
            => ClientLifecycleRegressionTests.RunOnSta(() =>
            {
                var sessions = SessionCollectionService.Instance;
                var first = new SessionModel { Id = Guid.NewGuid(), Name = "Chosen" };
                using var process = Process.GetCurrentProcess();
                var second = new SessionModel { Id = Guid.NewGuid(), Name = "Other", RSProcess = process };
                second.UpdateInjectionState(InjectionState.Injected);
                sessions.Sessions.Add(first); sessions.Sessions.Add(second);
                var enabled = Settings.Default.MesharpIntegrationEnabled;
                Settings.Default.MesharpIntegrationEnabled = false;
                using var vm = new ScriptManagerViewModel(new ScriptManagerService(() => "[]", _ => { }), sessions, new McpBridgeClientService());
                try
                {
                    vm.TargetSession = first;
                    var resolve = typeof(ScriptManagerViewModel).GetMethod("ResolveOrSelectTargetSession", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    Assert.Null(resolve.Invoke(vm, null));
                    Assert.Same(first, vm.TargetSession);
                    Assert.False(vm.StopScriptCommand.CanExecute(null));
                    sessions.Sessions.Remove(first);
                    Assert.Null(vm.TargetSession);
                    Assert.Null(resolve.Invoke(vm, null));
                }
                finally
                {
                    vm.Dispose();
                    sessions.Sessions.Remove(first); sessions.Sessions.Remove(second);
                    Settings.Default.MesharpIntegrationEnabled = enabled;
                }
            });

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ScriptDiagnostics_CancelledTargetOrDisposedToolCannotPublishOldResults(bool dispose)
            => await DeepAuditFixTests.RunOnStaAsync(async () =>
            {
                var sessions = SessionCollectionService.Instance;
                using var process = Process.GetCurrentProcess();
                var first = new SessionModel { Id = Guid.NewGuid(), Name = "Old", RSProcess = process };
                var second = new SessionModel { Id = Guid.NewGuid(), Name = "New" };
                sessions.Sessions.Add(first); sessions.Sessions.Add(second);
                var enabled = Settings.Default.MesharpIntegrationEnabled;
                var menu = Settings.Default.ShowMenuMcpControl;
                Settings.Default.MesharpIntegrationEnabled = true;
                Settings.Default.ShowMenuMcpControl = false;
                using var vm = new ScriptManagerViewModel(new ScriptManagerService(() => "[]", _ => { }), sessions, new McpBridgeClientService());
                using var pipe = new NamedPipeServerStream($"MESharpMcpBridge.{process.Id}", PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                try
                {
                    vm.TargetSession = first;
                    var accepted = pipe.WaitForConnectionAsync();
                    first.UpdateInjectionState(InjectionState.Injected);
                    await accepted.WaitAsync(TimeSpan.FromSeconds(3));
                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, leaveOpen: true);
                    Assert.Contains("script.list", await reader.ReadLineAsync());
                    Assert.True(vm.IsMcpToolEnabled); // Hiding a menu item is not disabling diagnostics.
                    if (dispose) vm.Dispose(); else vm.TargetSession = second;
                    await Task.Delay(100);
                    Assert.Empty(vm.RuntimeScripts);
                    if (!dispose)
                    {
                        Assert.Same(second, vm.TargetSession);
                        Assert.Contains("Select an injected session", vm.DiagnosticsStatus);
                    }
                }
                finally
                {
                    vm.Dispose();
                    sessions.Sessions.Remove(first); sessions.Sessions.Remove(second);
                    Settings.Default.MesharpIntegrationEnabled = enabled;
                    Settings.Default.ShowMenuMcpControl = menu;
                }
            });

        [Fact]
        public void ScriptRow_UsesItsOwnIdAndMissingFilesCannotFallBackToAnotherScript()
            => ClientLifecycleRegressionTests.RunOnSta(() =>
            {
                var enabled = Settings.Default.MesharpIntegrationEnabled;
                var previousPath = Settings.Default.HotReloadScriptPath;
                Settings.Default.MesharpIntegrationEnabled = false;
                using var vm = new ScriptManagerViewModel(new ScriptManagerService(() => "[]", _ => { }),
                    SessionCollectionService.Instance, new McpBridgeClientService());
                try
                {
                    var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "selected.dll");
                    File.WriteAllText(path, "synthetic file");
                    vm.HotReloadScriptPath = path;
                    vm.SelectedScriptId = "selected.id";
                    var missing = new ScriptProfile { FilePath = path + ".missing", ScriptId = "other.id" };
                    var resolvePath = typeof(ScriptManagerViewModel).GetMethod("ResolveExecutableProfile", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    var resolveId = typeof(ScriptManagerViewModel).GetMethod("ResolveScriptId", BindingFlags.NonPublic | BindingFlags.Instance)!;
                    Assert.Null(resolvePath.Invoke(vm, new object[] { missing }));
                    Assert.Equal("other.id", resolveId.Invoke(vm, new object[] { missing }));
                }
                finally
                {
                    vm.Dispose();
                    Settings.Default.MesharpIntegrationEnabled = enabled;
                    Settings.Default.HotReloadScriptPath = previousPath;
                }
            });

        [Fact]
        public void SessionsTool_ObservesConfiguredPathAndPlacementChanges()
        {
            var sessions = SessionCollectionService.Instance;
            var placement = new SessionPlacementService();
            var reconciliation = new SessionReconciliationService(sessions, placement, new TearOffHostRegistry());
            var layout = new OrbitLayoutStateService(sessions, placement, reconciliation);
            var scripts = new ScriptManagerService(() => "[]", _ => { });
            using var vm = new SessionsOverviewViewModel(new ObservableCollection<SessionModel>(), _ => { }, _ => { }, _ => { },
                _ => Task.CompletedTask, _ => false, reconciliation, layout,
                new ScriptOrchestrationService(scripts, ConsoleLogService.Instance, new OrbitCommandClientAdapter()), placement);
            var pathNotifications = 0;
            var placementNotifications = 0;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(vm.ConfiguredScriptPath)) pathNotifications++;
                if (e.PropertyName == nameof(vm.UnplacedSessionCount)) placementNotifications++;
            };
            var originalPath = Settings.Default.HotReloadScriptPath;
            try
            {
                var path = Path.Combine(ClientLifecycleRegressionTests.CreateFolder(), "script.dll");
                File.WriteAllText(path, "synthetic");
                Settings.Default.HotReloadScriptPath = path;
                Assert.True(vm.HasConfiguredScriptPath);
                Assert.True(pathNotifications > 0);
                placement.SetPlacement(new SessionModel { Id = Guid.NewGuid() }, SessionPlacementKind.MainTabs);
                Assert.True(placementNotifications > 0);
                vm.Dispose();
                var count = pathNotifications;
                Settings.Default.HotReloadScriptPath = originalPath;
                Assert.Equal(count, pathNotifications);
            }
            finally { Settings.Default.HotReloadScriptPath = originalPath; }
        }

        [Fact]
        public void McpTargets_TrackInjectionAndNameAndExcludeExternalWindows()
            => ClientLifecycleRegressionTests.RunOnSta(() =>
            {
                var sessions = SessionCollectionService.Instance;
                using var process = Process.GetCurrentProcess();
                var session = new SessionModel { Id = Guid.NewGuid(), Name = "Before", RSProcess = process };
                var external = new SessionModel { Id = Guid.NewGuid(), SessionType = SessionType.ExternalScript, RSProcess = process };
                external.UpdateInjectionState(InjectionState.Injected);
                sessions.Sessions.Add(session); sessions.Sessions.Add(external);
                var folder = ClientLifecycleRegressionTests.CreateFolder();
                using var vm = new McpControlCenterViewModel(sessions, new McpBridgeClientService(),
                    new McpPreferencesStore(Path.Combine(folder, "prefs.json")), new McpInjectorSettingsService(Path.Combine(folder, "injector.json")));
                try
                {
                    Assert.Empty(vm.Sessions);
                    session.UpdateInjectionState(InjectionState.Injected);
                    Assert.Equal(session.Id, Assert.Single(vm.Sessions).SessionId);
                    Assert.True(vm.CanInteractWithSession);
                    session.Name = "After";
                    Assert.Contains("After", vm.SelectedSessionSummary);
                    session.UpdateInjectionState(InjectionState.NotReady);
                    Assert.Empty(vm.Sessions);
                    Assert.False(vm.CanInteractWithSession);
                }
                finally
                {
                    vm.Dispose();
                    sessions.Sessions.Remove(session); sessions.Sessions.Remove(external);
                }
            });

        private sealed class TestTool(string key) : IOrbitTool
        {
            public string Key => key;
            public string DisplayName => key;
            public PackIconMaterialKind Icon => PackIconMaterialKind.Tools;
            public FrameworkElement CreateView(object? context = null) => throw new NotSupportedException();
        }
    }
}

namespace MESharp
{
    // Discovery must inspect this type's metadata, never invoke its initializer.
    public static class ScriptEntry
    {
        static ScriptEntry() => throw new InvalidOperationException("Metadata inspection executed script code.");
        public static void Initialize() { }
    }
}
