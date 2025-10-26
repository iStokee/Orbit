using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Orbit.Views;

namespace Orbit.Services;

public sealed class SettingsWindowManager
{
    private static readonly Lazy<SettingsWindowManager> _lazy = new(() => new SettingsWindowManager());
    private readonly List<SettingsWindow> _openWindows = new();

    private SettingsWindowManager() { }

    public static SettingsWindowManager Instance => _lazy.Value;

    public void OpenOrFocus(Window owner = null)
    {
        CleanupClosedWindows();

        var existing = _openWindows.FirstOrDefault();
        if (existing != null)
        {
            existing.Activate();
            existing.Focus();
            return;
        }

        var window = new SettingsWindow
        {
            Owner = owner,
            DataContext = owner?.DataContext
        };

        window.Closed += OnWindowClosed;
        _openWindows.Add(window);
        window.Show();
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        if (sender is SettingsWindow w)
        {
            w.Closed -= OnWindowClosed;
            _openWindows.Remove(w);
        }
    }

    private void CleanupClosedWindows()
    {
        _openWindows.RemoveAll(w => w == null || !w.IsLoaded || PresentationSource.FromVisual(w) == null);
    }
}
