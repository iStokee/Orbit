# MahApps to WPF UI Migration Roadmap

## Goal
Replace MahApps shell/theming dependencies with WPF UI while preserving Orbit's core behavior:
- session launch/inject lifecycle
- tab tear-off and drag docking
- orbit workspace orchestration

## Guiding Strategy
1. Keep behavior engines stable (`Dragablz`, session orchestration, injection pipeline).
2. Replace visual shell and UI primitives in slices.
3. Move all new surfaces to semantic `Orbit.*` tokens first, then remove MahApps resource keys.

## Phase 1 (Started)
- Add `WPF-UI` package to Orbit project.
- Introduce migration tokens dictionary (`Resources/OrbitWinUiTokens.xaml`).
- Add `ConstellationBoard` as a new first-class tool surface to validate a new interaction model.
- Wire tool into registry and floating menu.

## Phase 2
- Convert auxiliary windows (`SettingsWindow`, `LauncherAccountConfigWindow`, `SessionCloseDialog`) off `MetroWindow`.
- Replace MahApps-only controls/styles in those windows.
- Route dialogs/snackbars through an abstraction (service interface) instead of direct MahApps APIs.

## Phase 3
- Migrate `MainWindow` shell from `MetroWindow` to WPF UI window primitives.
- Replace title bar/flyout/floating menu shell styles to Orbit semantic tokens.
- Keep tab/docking logic unchanged during this phase.

## Phase 4
- Remove `MahApps.Metro` and `MahApps.Metro.IconPacks` package dependencies.
- Final resource key cleanup (`MahApps.*` to `Orbit.*`).
- Regression pass: launch/inject, tear-off/re-dock, Orbit View, Constellation Board, plugins.

## Constellation Board Vision
Constellation Board is a session-centric command surface:
- each session is rendered as a "sun"
- hovering/selecting expands a radial command constellation
- pan + zoom infinite-feel workspace
- action satellites trigger session operations (focus, inject, scripts, orbit view, console, close)

This is intentionally additive to current tabs/docking and can become the default control room once stabilized.
