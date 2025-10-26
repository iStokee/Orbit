using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Orbit.Views;

namespace Orbit.Services;

public sealed class ConsoleWindowManager
{
	private static readonly Lazy<ConsoleWindowManager> _lazy = new(() => new ConsoleWindowManager());
	private readonly List<ConsoleHostWindow> _openWindows = new();

	private ConsoleWindowManager()
	{
	}

	public static ConsoleWindowManager Instance => _lazy.Value;

	/// <summary>
	/// Opens a new console window or focuses an existing one.
	/// </summary>
	/// <param name="owner">The owner window for the console window</param>
	public void OpenOrFocusConsole(Window owner = null)
	{
		CleanupClosedWindows();

		// If there's already an open console window, focus it
		var existingWindow = _openWindows.FirstOrDefault();
		if (existingWindow != null)
		{
			existingWindow.Activate();
			existingWindow.Focus();
			return;
		}

		// Otherwise, create a new console window
		var window = new ConsoleHostWindow
		{
			Owner = owner
		};

		window.Closed += OnWindowClosed;
		_openWindows.Add(window);
		window.Show();
	}

	/// <summary>
	/// Creates a new console window regardless of existing windows.
	/// </summary>
	/// <param name="owner">The owner window for the console window</param>
	public void OpenNewConsole(Window owner = null)
	{
		CleanupClosedWindows();

		var window = new ConsoleHostWindow
		{
			Owner = owner
		};

		window.Closed += OnWindowClosed;
		_openWindows.Add(window);
		window.Show();
	}

	private void OnWindowClosed(object sender, EventArgs e)
	{
		if (sender is ConsoleHostWindow window)
		{
			window.Closed -= OnWindowClosed;
			_openWindows.Remove(window);
		}
	}

	private void CleanupClosedWindows()
	{
		_openWindows.RemoveAll(w => w == null || !w.IsLoaded || PresentationSource.FromVisual(w) == null);
	}
}
