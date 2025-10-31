using System;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Logging;
using Orbit.Services;
using Orbit.Tooling;
using Orbit.ViewModels;
using Orbit.Views;
using Application = System.Windows.Application;

namespace Orbit;

public partial class App : Application
{
	private ServiceProvider? _serviceProvider;
	private ConsolePipeServer? _pipeServer;

	public IServiceProvider Services =>
		_serviceProvider ?? throw new InvalidOperationException("Service provider has not been initialized yet.");

	protected override void OnStartup(StartupEventArgs e)
	{
		base.OnStartup(e);

		var services = new ServiceCollection();
		ConfigureServices(services);

		_serviceProvider = services.BuildServiceProvider();

		var consoleLog = _serviceProvider.GetRequiredService<ConsoleLogService>();
		consoleLog.StartCapture();

		_pipeServer = _serviceProvider.GetRequiredService<ConsolePipeServer>();
		_pipeServer.Start();

		var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
		MainWindow = mainWindow;
		mainWindow.Show();
	}

	protected override void OnExit(ExitEventArgs e)
	{
		_pipeServer?.Dispose();
		Settings.Default.Save();

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

		services.AddSingleton<SessionCollectionService>(_ => SessionCollectionService.Instance);
		services.AddSingleton<ScriptIntegrationService>();
		services.AddSingleton<SessionManagerService>();
		services.AddSingleton<ThemeService>();
		services.AddSingleton<ScriptManagerService>();
		services.AddSingleton<AccountService>();
		services.AddSingleton<AutoLoginService>();
		services.AddSingleton<InterTabClient>();

		services.AddTransient<ScriptControlsView>();
		services.AddTransient<SettingsView>();
		services.AddTransient<ConsoleView>();
		services.AddTransient<ThemeManagerViewModel>();
		services.AddTransient<ThemeManagerPanel>(sp => new ThemeManagerPanel(sp.GetRequiredService<ThemeManagerViewModel>()));
		services.AddTransient<ScriptManagerViewModel>();
		services.AddTransient<ScriptManagerPanel>(sp => new ScriptManagerPanel(sp.GetRequiredService<ScriptManagerViewModel>()));

		services.AddTransient<AccountManagerViewModel>(sp => new AccountManagerViewModel(
			sp.GetRequiredService<AccountService>(),
			sp.GetRequiredService<SessionCollectionService>(),
			sp.GetRequiredService<AutoLoginService>()));
		services.AddTransient<AccountManagerView>();

		services.AddSingleton<IOrbitTool, ScriptControlsTool>();
		services.AddSingleton<IOrbitTool, SettingsTool>();
		services.AddSingleton<IOrbitTool, ConsoleTool>();
		services.AddSingleton<IOrbitTool, ThemeManagerTool>();
		services.AddSingleton<IOrbitTool, SessionsOverviewTool>();
		services.AddSingleton<IOrbitTool, ScriptManagerTool>();
		services.AddSingleton<IOrbitTool, AccountManagerTool>();
		services.AddSingleton<IOrbitTool, Tooling.BuiltInTools.ApiDocumentationTool>();
		services.AddSingleton<IOrbitTool, Tooling.BuiltInTools.ToolsOverviewTool>();
		services.AddSingleton<IToolRegistry, ToolRegistry>();

		services.AddTransient<MainWindowViewModel>();
		services.AddTransient<MainWindow>();
	}
}
