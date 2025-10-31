using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class AccountManagerTool : IOrbitTool
{
	private readonly IServiceProvider _serviceProvider;

	public AccountManagerTool(IServiceProvider serviceProvider)
	{
		_serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
	}

	public string Key => "AccountManager";

	public string DisplayName => "Account Manager";

	public PackIconMaterialKind Icon => PackIconMaterialKind.AccountMultiple;

    public FrameworkElement CreateView(object? context = null)
    {
        return _serviceProvider.GetRequiredService<AccountManagerView>();
    }
}
