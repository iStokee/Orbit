using System;
using System.Threading.Tasks;
using System.Linq;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Logging;
using Orbit.Services;
using Orbit.Tooling;
using Orbit.ViewModels;
using Orbit.Views;
using Application = System.Windows.Application;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;

namespace Orbit;

public partial class App : Application
{
	private ServiceProvider? _serviceProvider;
	private ConsolePipeServer? _pipeServer;
	private OrbitApiPipeServer? _orbitApiPipeServer;

	public IServiceProvider Services =>
		_serviceProvider ?? throw new InvalidOperationException("Service provider has not been initialized yet.");

	public bool IsShutdownPrepared { get; private set; }
	public bool IsShuttingDown => Services.GetRequiredService<ApplicationShutdownService>().IsShuttingDown;
	public async Task PrepareForShutdownAsync()
	{
		await Services.GetRequiredService<ApplicationShutdownService>().PrepareAsync();
		IsShutdownPrepared = true;
	}

	private void PreparePresentationForShutdown()
	{
		Services.GetRequiredService<ScriptIntegrationService>().IsShuttingDown = true;
		_orbitApiPipeServer?.Stop();
		Services.GetRequiredService<Plugins.PluginManager>().BeginShutdown();
		var rootTools = new System.Collections.Generic.HashSet<Models.ToolTabItem>();
		foreach (var window in Windows.OfType<MainWindow>().ToArray())
			if (window.DataContext is MainWindowViewModel vm)
			{
				foreach (var tool in vm.Tabs.OfType<Models.ToolTabItem>()) rootTools.Add(tool);
				vm.MarkApplicationShuttingDown();
				vm.Dispose();
			}
		var otherTools = Services.GetRequiredService<OrbitLayoutStateService>().Items
			.Concat(Windows.OfType<MainWindow>().SelectMany(window => Utilities.VisualTreeUtil.FindVisualChildren<Dragablz.TabablzControl>(window))
				.SelectMany(control => control.Items.Cast<object>()))
			.OfType<Models.ToolTabItem>().Distinct().Where(tool => !rootTools.Contains(tool)).ToArray();
		foreach (var tool in otherTools)
			Services.GetRequiredService<ShellToolHostService>().DisposeToolItem(tool, this);
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var services = new ServiceCollection();
		ConfigureServices(services);

		_serviceProvider = services.BuildServiceProvider();
		_serviceProvider.GetRequiredService<SessionManagerService>().StartTracking(SessionCollectionService.Instance);
		ApplyUpdateBadgeBrushFromSettings();

		var consoleLog = _serviceProvider.GetRequiredService<ConsoleLogService>();
		consoleLog.StartCapture();
		OrbitInteractionLogger.IsEnabled = Settings.Default.OrbitInteractionLoggingEnabled;

		// Force-instantiate the ownership coordinator so it subscribes to placement changes and
		// enforces the single-host invariant (resolves ghost-duplicate sessions).
		_serviceProvider.GetRequiredService<SessionOwnershipCoordinatorService>();

		_pipeServer = _serviceProvider.GetRequiredService<ConsolePipeServer>();
		_pipeServer.Start();
		_orbitApiPipeServer = _serviceProvider.GetRequiredService<OrbitApiPipeServer>();
		_orbitApiPipeServer.Start();

		var mainWindow = _serviceProvider.GetRequiredService<OwnedViewFactory>().Create<MainWindow>();
		MainWindow = mainWindow;
		mainWindow.Show();

		_ = AutoLoadPluginsAsync();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_pipeServer?.Dispose();
		_orbitApiPipeServer?.Dispose();
		OrbitInteractionLogger.Shutdown();
		try
		{
			ConsoleLogService.Instance.StopCapture();
		}
		catch
		{
			// best effort shutdown cleanup
		}

		try
		{
			Settings.Default.Save();
		}
		catch
		{
			// best effort settings persistence
		}

		if (_serviceProvider is IDisposable disposable)
		{
			disposable.Dispose();
		}

		base.OnExit(e);
	}

	private static void ConfigureServices(IServiceCollection services)
	{
		services.AddSingleton<ConsoleLogService>(_ => ConsoleLogService.Instance);
		services.AddSingleton<ConsolePipeServer>();
		services.AddSingleton<OrbitApiPipeServer>();
		services.AddSingleton<FloatingMenuGeometryService>();
		services.AddSingleton<FloatingMenuVisibilityService>();
		services.AddSingleton<FloatingMenuQuickToggleService>();
		services.AddSingleton<TaskbarOrbitIconService>();

		services.AddSingleton<SessionCollectionService>(_ => SessionCollectionService.Instance);
		services.AddSingleton<SessionAutoRelaunchService>();
		services.AddSingleton<SessionLaunchCoordinatorService>();
		services.AddSingleton<SessionLifecycleCoordinatorService>();
		services.AddSingleton<SessionNameService>();
		services.AddSingleton<SessionPlacementService>();
		services.AddSingleton<SessionRenameService>();
		services.AddSingleton<SessionReconciliationService>();
		services.AddSingleton<SessionStartupService>();
		services.AddSingleton<SessionTargetResolverService>();
		services.AddSingleton<SessionUiCoordinatorService>();
		services.AddSingleton<SessionOwnershipCoordinatorService>();
		services.AddSingleton<ShellPresentationPolicyService>();
		services.AddSingleton<ShellSessionCloseService>();
		services.AddSingleton<ShellSessionFocusService>();
		services.AddSingleton<ShellSessionRecoveryService>();
		services.AddSingleton<ShellSessionSelectionService>();
		services.AddSingleton<ShellTabCollectionCoordinatorService>();
		services.AddSingleton<ShellTabSelectionService>();
		services.AddSingleton<ShellToolHostService>();
		services.AddSingleton<ShellToolCoordinatorService>();
		services.AddSingleton<ShellWindowPlacementService>();
		services.AddSingleton<ShellWindowTransferService>();
		services.AddSingleton<OrbitLayoutStateService>();
		services.AddSingleton<ScriptIntegrationService>();
		services.AddSingleton<IOrbitCommandClient, OrbitCommandClientAdapter>();
		services.AddSingleton<MesharpSessionCommandService>();
		services.AddSingleton<ScriptOrchestrationService>();
		services.AddSingleton<InjectorPathResolver>();
		services.AddSingleton<SessionManagerService>();
		services.AddSingleton<ApplicationShutdownService>(sp => new ApplicationShutdownService(
			sp.GetRequiredService<SessionCollectionService>(), sp.GetRequiredService<SessionManagerService>(),
			sp.GetRequiredService<SessionPlacementService>(), sp.GetRequiredService<OrbitLayoutStateService>(),
			sp.GetRequiredService<Plugins.PluginManager>(), () => ((App)Current).PreparePresentationForShutdown()));
		services.AddSingleton<McpPreferencesStore>();
		services.AddSingleton<McpBridgeClientService>();
		services.AddSingleton<McpInjectorSettingsService>();
		services.AddSingleton<ThemeService>();
		services.AddSingleton<ScriptManagerService>();
		services.AddSingleton<AccountService>();
		services.AddSingleton<TearOffHostRegistry>();
		services.AddSingleton<InterTabClient>();
		services.AddSingleton<MesharpHotkeyService>();

			services.AddSingleton<SettingsViewModel>();
		services.AddSingleton<OwnedViewFactory>();

		services.AddSingleton<IOrbitTool, SettingsTool>();
		services.AddSingleton<IOrbitTool, ConsoleTool>();
		services.AddSingleton<IOrbitTool, ThemeManagerTool>();
		services.AddSingleton<IOrbitTool, SessionsOverviewTool>();
		services.AddSingleton<IOrbitTool, ScriptManagerTool>();
		services.AddSingleton<IOrbitTool, AccountManagerTool>();
		services.AddSingleton<IOrbitTool, McpControlTool>();
		services.AddSingleton<IOrbitTool>(sp => new SessionGalleryTool(sp.GetRequiredService<SessionCollectionService>()));
		services.AddSingleton<IOrbitTool>(sp => new OrbitViewTool(
			sp.GetRequiredService<SessionCollectionService>(),
			sp.GetRequiredService<OrbitLayoutStateService>(),
			sp.GetRequiredService<TearOffHostRegistry>(),
			sp.GetRequiredService<SessionReconciliationService>()));
		services.AddSingleton<IOrbitTool, Tooling.BuiltInTools.GuideTool>();
		// Legacy separate tools (kept for compatibility, but UnifiedToolsManagerTool combines them)
		// New unified dashboard
		services.AddSingleton<IOrbitTool, Tooling.BuiltInTools.UnifiedToolsManagerTool>();
		services.AddSingleton<IToolRegistry, ToolRegistry>();

		// Plugin system
		services.AddSingleton<Plugins.PluginManager>(sp =>
		{
			var toolRegistry = sp.GetRequiredService<IToolRegistry>();
			var pluginDirectory = Plugins.PluginManager.GetDefaultPluginDirectory();
			return new Plugins.PluginManager(toolRegistry, pluginDirectory, sp.GetRequiredService<OrbitLayoutStateService>());
		});

		
		
	}

	private static void ApplyUpdateBadgeBrushFromSettings()
	{
		var configured = Settings.Default.UpdateBadgeColorHex;
		if (!TryParseColor(configured, out var color))
		{
			if (!TryParseColor("#FFFFC44D", out color))
			{
				return;
			}
		}

		var app = Current;
		if (app == null)
		{
			return;
		}

		const string key = "Orbit.UpdateBadgeBrush";
		if (app.Resources[key] is SolidColorBrush existing)
		{
			if (!existing.IsFrozen)
			{
				existing.Color = color;
				return;
			}
		}

		app.Resources[key] = new SolidColorBrush(color);
	}

	private static bool TryParseColor(string? value, out MediaColor color)
	{
		color = default;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		try
		{
			var parsed = MediaColorConverter.ConvertFromString(value);
			if (parsed is MediaColor resolved)
			{
				color = resolved;
				return true;
			}
		}
		catch
		{
			// Ignore malformed values and fall back to default.
		}

		return false;
	}

	private async Task AutoLoadPluginsAsync()
	{
		if (_serviceProvider == null)
		{
			return;
		}

		try
		{
			var pluginManager = _serviceProvider.GetRequiredService<Plugins.PluginManager>();
			var loaded = await pluginManager.AutoLoadPluginsAsync().ConfigureAwait(true);
			var consoleLog = _serviceProvider.GetRequiredService<ConsoleLogService>();
			consoleLog.Append(
				$"[Orbit] Plugin auto-load completed. Loaded {loaded} plugin(s).",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Info);
		}
		catch (Exception ex)
		{
			var consoleLog = _serviceProvider.GetService<ConsoleLogService>();
			consoleLog?.Append(
				$"[Orbit] Plugin auto-load failed: {ex.Message}",
				ConsoleLogSource.Orbit,
				ConsoleLogLevel.Warning);
		}
	}
}
