# Orbit

Modern WPF control surface for MemoryError. Orbit launches RuneScape clients, manages session injection, hosts tooling, and supports embedding external script windows through OrbitAPI.

## Overview

Orbit is the operational UI for MemoryError workflows:

- Launch and manage multiple RS3 sessions
- Inject `XInput1_4_inject.dll` into each session
- Embed client windows directly in Orbit layouts/tabs
- Load/reload script assemblies and monitor runtime status
- Provide centralized logs (Orbit, ME, scripts)
- Manage tools/plugins via a unified dashboard in Settings

## Current Runtime and Build Targets

- Target framework: `net10.0-windows8.0`
- Platform target: `x64`
- UI stack: WPF + MahApps + Dragablz
- DI container: `Microsoft.Extensions.DependencyInjection`

## Quick Start

1. Launch `Orbit.exe`.
2. Open **Settings -> General -> Client Launch**.
3. Choose launch mode:
   - `Legacy Executable Paths`
   - `Jagex Launcher URI`
4. If using Launcher mode, click **Config** and select one or more Jagex accounts.
5. Create sessions from the main UI and inject (or use auto-inject).
6. Open **Settings -> Advanced** to enable debug logs when needed.

## Client Launch Modes

Orbit supports both legacy and Jagex-launcher flows.

- `Legacy Executable Paths`
  - Starts a regular client executable from known paths.
  - Best match for BasicInjector `Launch_RS` behavior.

- `Jagex Launcher URI`
  - Starts via `rs-launch://.../jav_config.ws`.
  - Applies account `JX_*` environment variables from launcher config.
  - Supports multi-account selection from config.

## Jagex Account Config Behavior

`Config` in **Settings -> General -> Client Launch** reads/writes `env_vars.json` account entries and selection flags.

Behavior mapping relative to BasicInjector button model:

- **Single selected account** -> equivalent of `Launch_1_JX`
- **All accounts selected** -> equivalent of `Launch_All_JX`
- **Custom selected subset** -> equivalent of `Config + Launch_Custom_JX`

Orbit launches selected accounts in round-robin order per new session launch.

## Multi-Session Injection Notes

For launcher mode, Orbit now protects against common multi-launch issues by:

- serializing launcher starts briefly so each process captures the intended `JX_*` values
- avoiding duplicate `rs2client` PID binding across sessions
- refusing injection when another active session already owns that PID

If only one session injects correctly, enable logs and verify PID/account mapping per launch.

## Logging and Diagnostics

Orbit has dedicated diagnostics in **Settings -> Advanced**:

- `Enable theme debug logging`
- `Enable Orbit interaction logging (drag/drop/reparent)`

Logs are written under Orbit's local logs path (relative to app/runtime storage). Use the built-in **Open log file** actions in Settings for direct access.

## Settings Layout (Current)

Settings tabs are currently organized as:

1. `General`
2. `Floating Menu`
3. `Orbit View`
4. `Tools & Plugins`
5. `Advanced`

Notable details:

- Client launch mode + launcher account config are in **General** (top section)
- Tools/plugins dashboard is a dedicated **Settings** tab
- Orbit Builder is registered but hidden from default floating menu exposure

## Script Integration (OrbitAPI)

External script UIs can be embedded in Orbit tabs via `Orbit.API.OrbitAPI`.

Typical flow:

1. Check availability: `OrbitAPI.IsOrbitAvailable()`
2. Register window handle: `OrbitAPI.RegisterScriptWindow(...)`
3. Unregister on shutdown: `OrbitAPI.UnregisterScriptWindow(...)`

## Build

From `Orbit/Orbit`:

```bash
dotnet build Orbit.csproj -c Debug
dotnet build Orbit.csproj -c Release
```

Run debug build:

```bash
dotnet run -c Debug
```

## Core Services

- `SessionCollectionService`: active session registry/state
- `SessionManagerService`: launch/inject/cleanup pipeline
- `OrbitLayoutStateService`: shared layout state for Orbit View
- `ScriptManagerService`: script load/reload coordination
- `OrbitCommandClient`: command channel to ME runtime
- `ConsolePipeServer`: incoming log/pipe integration
- `OrbitApiPipeServer`: OrbitAPI IPC endpoint
- `ThemeService`: theme persistence and application

## Versioning and Updates

Orbit version values are defined in `Versioning/AppVersion.cs`.

- `Current` (semantic base)
- `AssemblyVersion`/`FileVersion` (full display + updater comparison)

Settings displays the full app version string from `AppVersion.Display`.

## Troubleshooting

- **Launcher sessions open but wrong account/session attaches**
  - Reopen **General -> Client Launch -> Config** and confirm selected entries.
  - Launch sessions sequentially once to verify round-robin order.

- **Multiple sessions launch but only one injects**
  - Check logs for duplicate PID warnings.
  - Ensure each session resolves a distinct `rs2client` PID before injection.

- **Drag/tab interactions feel unstable**
  - Enable Orbit interaction logging in **Advanced** and inspect events around drag/reparent operations.

## Repository Notes

Orbit lives under `Orbit/Orbit` in the MemoryError repository. Additional operator docs are under `docs/OrbitersGuide`.
