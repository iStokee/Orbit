using System;
using Microsoft.Extensions.DependencyInjection;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Services;

/// <summary>UI objects are owned by their tool/window, not captured as root DI transients.</summary>
public sealed class OwnedViewFactory
{
	private readonly IServiceProvider _services;
	public OwnedViewFactory(IServiceProvider services) => _services = services;
	public T CreateModel<T>() where T : class => ActivatorUtilities.CreateInstance<T>(_services);
	public T Create<T>() where T : class
	{
		object view = typeof(T) == typeof(MainWindow) ? ActivatorUtilities.CreateInstance<MainWindow>(_services, CreateModel<MainWindowViewModel>())
			: typeof(T) == typeof(SettingsView) ? new SettingsView(_services.GetRequiredService<SettingsViewModel>(), _services.GetRequiredService<Orbit.Tooling.IToolRegistry>())
			: typeof(T) == typeof(ScriptManagerPanel) ? new ScriptManagerPanel(CreateModel<ScriptManagerViewModel>())
			: typeof(T) == typeof(GuideBrowserView) ? new GuideBrowserView(CreateModel<GuideBrowserViewModel>())
			: typeof(T) == typeof(McpControlCenterView) ? new McpControlCenterView(CreateModel<McpControlCenterViewModel>())
			: typeof(T) == typeof(AccountManagerView) ? new AccountManagerView(CreateModel<AccountManagerViewModel>())
			: typeof(T) == typeof(ThemeManagerPanel) ? new ThemeManagerPanel(CreateModel<ThemeManagerViewModel>())
			: ActivatorUtilities.CreateInstance<T>(_services);
		return (T)view;
	}
}
