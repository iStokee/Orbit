using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Orbit.Views;

namespace Orbit.Services;

public sealed class ScriptControlsWindowManager
{
    private static readonly Lazy<ScriptControlsWindowManager> _lazy = new(() => new ScriptControlsWindowManager());
    private readonly List<ScriptControlsWindow> _openWindows = new();

    private ScriptControlsWindowManager() { }

    public static ScriptControlsWindowManager Instance => _lazy.Value;

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

        var window = new ScriptControlsWindow
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
        if (sender is ScriptControlsWindow w)
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
