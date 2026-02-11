using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Dragablz;
using Application = System.Windows.Application;

namespace Orbit.Services;

/// <summary>
/// Tracks Dragablz tear-off host windows so Orbit View can deterministically clean up/re-home
/// only the hosts that originated from Orbit View (and not user-detached main tabs).
/// </summary>
public sealed class TearOffHostRegistry
{
	public enum HostOrigin
	{
		Unknown = 0,
		MainTabs = 1,
		OrbitView = 2
	}

	private sealed record HostEntry(Window Window, TabablzControl TabControl, string Partition, HostOrigin Origin);

	private readonly object _sync = new();
	private readonly List<HostEntry> _hosts = new();

	public void Register(Window window, TabablzControl tabControl, string partition, HostOrigin origin)
	{
		if (window == null) throw new ArgumentNullException(nameof(window));
		if (tabControl == null) throw new ArgumentNullException(nameof(tabControl));

		partition ??= string.Empty;

			lock (_sync)
			{
				_hosts.RemoveAll(h => ReferenceEquals(h.Window, window));
				_hosts.Add(new HostEntry(window, tabControl, partition, origin));
			}

			void OnClosed(object? sender, EventArgs e)
			{
				window.Closed -= OnClosed;
				Unregister(window);
			}

		window.Closed -= OnClosed;
		window.Closed += OnClosed;
	}

	public void Unregister(Window window)
	{
		if (window == null) return;

		lock (_sync)
		{
			_hosts.RemoveAll(h => ReferenceEquals(h.Window, window));
		}
	}

	public IReadOnlyList<(Window Window, TabablzControl TabControl)> GetHosts(string partition, HostOrigin origin)
	{
		partition ??= string.Empty;

		lock (_sync)
		{
			return _hosts
				.Where(h => string.Equals(h.Partition, partition, StringComparison.Ordinal) && h.Origin == origin)
				.Select(h => (h.Window, h.TabControl))
				.ToList();
		}
	}

	public void CloseHosts(string partition, HostOrigin origin)
	{
		foreach (var (window, _) in GetHosts(partition, origin))
		{
			if (ReferenceEquals(window, Application.Current?.MainWindow))
			{
				continue;
			}

			try { window.Close(); } catch { /* best effort */ }
		}
	}
}
