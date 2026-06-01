using System;
using System.Linq;
using System.Windows;
using Orbit.Models;
using Orbit.ViewModels;
using Application = System.Windows.Application;

namespace Orbit.Services;

public sealed class ShellSessionFocusService
{
	public MainWindowViewModel ResolveOwningShellForSession(MainWindowViewModel currentShell, SessionModel session)
	{
		if (currentShell == null)
		{
			throw new ArgumentNullException(nameof(currentShell));
		}

		if (session == null)
		{
			throw new ArgumentNullException(nameof(session));
		}

		if (currentShell.Tabs.Contains(session))
		{
			return currentShell;
		}

		var hostWindow = session.HostControl != null ? Window.GetWindow(session.HostControl) : null;
		if (hostWindow?.DataContext is MainWindowViewModel hostVm)
		{
			return hostVm;
		}

		var windows = Application.Current?.Windows?.OfType<Window>() ?? Enumerable.Empty<Window>();
		foreach (var window in windows)
		{
			if (window.DataContext is not MainWindowViewModel candidate)
			{
				continue;
			}

			if (candidate.Tabs.Contains(session))
			{
				return candidate;
			}

			var orbitOwnership = candidate.Tabs.OfType<ToolTabItem>()
				.Any(tool =>
					string.Equals(tool.Key, ShellPresentationPolicyService.OrbitViewToolKey, StringComparison.Ordinal) &&
					ReferenceEquals(Window.GetWindow(session.HostControl), window));
			if (orbitOwnership)
			{
				return candidate;
			}
		}

		return currentShell;
	}

	public void RevealSession(MainWindowViewModel shell, SessionModel session, bool requestFocus)
	{
		if (shell == null || session == null)
		{
			return;
		}

		shell.SelectedSession = session;

		if (shell.Tabs.Contains(session))
		{
			shell.SelectedTab = session;
		}
		else
		{
			var orbitTab = shell.Tabs.OfType<ToolTabItem>()
				.FirstOrDefault(t => string.Equals(t.Key, ShellPresentationPolicyService.OrbitViewToolKey, StringComparison.Ordinal));
			if (orbitTab != null)
			{
				shell.SelectedTab = orbitTab;
			}
		}

		var dispatcher = session.HostControl?.Dispatcher ?? Application.Current?.Dispatcher;
		_ = dispatcher?.InvokeAsync(() =>
		{
			try
			{
				session.HostControl?.EnsureActiveAfterLayout();
				if (requestFocus)
				{
					session.HostControl?.FocusEmbeddedClient();
					session.SetFocus();
				}
			}
			catch
			{
				// Best effort.
			}
		}, System.Windows.Threading.DispatcherPriority.Input);
	}

	public void ActivateOwningWindow(MainWindowViewModel shell)
	{
		try
		{
			var owningWindow = Application.Current?.Windows
				?.OfType<Window>()
				.FirstOrDefault(window => ReferenceEquals(window.DataContext, shell));
			owningWindow?.Activate();
			owningWindow?.Focus();
		}
		catch
		{
			// Best effort.
		}
	}
}
