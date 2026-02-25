# Orbit WPF-UI Migration Audit (Startup -> MainWindow -> Tools)

Date: 2026-02-25

## Scope
Audit starts at app bootstrap (`App.xaml`/`App.xaml.cs`), flows through shell (`MainWindow`), then tool views and theming internals.

## Current State Summary
- WPF-UI window primitives are now in place (`FluentWindow` in shell and key dialogs).
- The visual/resource layer is still primarily MahApps-keyed (`MahApps.Brushes.*`, `MahApps.Colors.*`) across most views.
- Large portions of UI still use MahApps controls (`MetroTabControl`, `ToggleSwitch`, `NumericUpDown`, `ProgressRing`).
- Theme management has been refactored in this slice to be WPF-UI base-theme driven with Orbit-managed accent resources.

## Startup Path Findings
1. `App.xaml` still merges MahApps dictionaries and Dragablz MahApps theme.
2. `App.xaml` provides compatibility style keys (`MahApps.Styles.*`) that many views still depend on.
3. `App.xaml.cs` DI/startup is clean and does not depend on MahApps runtime APIs.

## Main Shell Findings
1. `MainWindow.xaml` is `ui:FluentWindow` but still references MahApps resources heavily.
2. `MainWindow.xaml` still includes MahApps designer dictionary in `d:Window.Resources`.
3. Tab/docking visuals are still driven by MahApps color keys and legacy style aliases.

## Theme System Findings
1. Previous implementation depended on `ControlzEx.ThemeManager` and MahApps theme objects.
2. Theme UI (`ThemeManagerPanel`) used MahApps `MetroTabControl`.
3. Settings/theme references still assume MahApps key names in many bindings.

## What Changed In This Slice
1. `ThemeService` now applies base theme via `Wpf.Ui.Appearance.ApplicationThemeManager` (Light/Dark).
2. Built-in accent presets are now Orbit-owned (internal scheme map), then projected into compatibility keys.
3. Custom themes still work, but are now layered on top of WPF-UI base theme instead of `ThemeManager.Current`.
4. `ThemeManagerPanel` tabs migrated from `MetroTabControl/MetroTabItem` to native `TabControl/TabItem`.

## Hotspots Remaining (High Priority)
1. `Views/SettingsView.xaml`: 90+ MahApps usages, heavy `MetroTabControl` and `ToggleSwitch` usage.
2. `MainWindow.xaml`: shell still coupled to MahApps resources for primary styling.
3. `Views/ConsoleView.xaml`: still uses `MetroTabControl`.
4. `Views/UnifiedToolsManagerView.xaml`, `Views/ToolsOverviewView.xaml`, `Views/McpControlCenterView.xaml`: still use MahApps toggle/progress controls.
5. `Views/OrbitGridLayoutView.xaml`, `Views/SessionGalleryView.xaml`, `Views/AccountManagerView.xaml`: still use MahApps `NumericUpDown` and toggles.

## Proposed Next Migration Slices
1. Replace all `MetroTabControl/MetroTabItem` with native `TabControl/TabItem` in `SettingsView` and `ConsoleView`.
2. Introduce Orbit/WPF-UI-native toggle style and migrate off MahApps `ToggleSwitch` (prefer `CheckBox` where behavior is binary).
3. Replace MahApps `NumericUpDown` with `IntegerUpDown` from Extended WPF Toolkit (already referenced) or custom spinner.
4. Replace MahApps `ProgressRing` with WPF-UI `ProgressRing` where available (or indeterminate `ProgressBar`).
5. After control migration, reduce MahApps dictionaries in `App.xaml` to color compatibility only, then remove package dependency.

## Risk Notes
1. Removing MahApps dictionaries too early will break many `DynamicResource` keys currently used by styles/templates.
2. Theme behavior is now split between WPF-UI base theme and Orbit compatibility resources; this is intentional as an interim bridge.
3. Dragablz MahApps theme dependency still exists and must be replaced once tab styling is fully tokenized.
