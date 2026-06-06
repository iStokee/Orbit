# Orbit

Modern WPF control surface for RuneScape session management. Orbit launches RuneScape clients, manages session injection, hosts tooling, and supports embedding external script windows through OrbitAPI.

## Overview

Orbit is the operational UI for automation workflows:

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

## UI Style Contract

Orbit uses the **Orbit Workbench Console** style: a dark-first, theme-aware desktop workbench for sessions, scripts, plugins, logs, settings, and runtime control.

The canonical UI contract lives in [docs/UI_STYLE_GUIDE.md](docs/UI_STYLE_GUIDE.md). New and refactored views should use shared `Orbit.Tool*` and `Orbit.Workbench.*` resources from `App.xaml` for page roots, headers, command bars, panels, repeated cards, section titles, helper text, metadata, badges, and buttons.

## Quick Start

1. Launch `Orbit.exe`.
2. Open **Settings -> General -> Client Launch**.
3. Choose launch mode:
   - `Legacy Executable Paths`
   - `Jagex Launcher URI`
4. If using Launcher mode, click **Config** and select one or more Jagex accounts.
5. (Optional) choose a custom injector DLL in **Settings -> General -> Injector DLL**.
6. Create sessions from the main UI and inject (or use auto-inject).
7. Open **Settings -> Advanced** to enable debug logs when needed.

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
When multiple accounts are selected and launch mode is `Jagex Launcher URI`, a single **Add Session** action launches one Orbit session per selected account.

## Account Password Storage

Account Manager passwords are stored with Windows DPAPI current-user protection before being written to Orbit's AppData account file. Existing plaintext `password` entries are migrated to `encryptedPassword` the next time Orbit loads the account file.

Important security model:

- Orbit does not ship or hide an encryption key.
- The encrypted account file is bound to the current Windows user profile.
- A process already running as the same Windows user can still ask Windows to decrypt the data, so this protects against offline file disclosure, not a compromised account/session.
- Community C# scripts and Orbit plugins are full-trust code. Treat them like executable programs: they can use filesystem, networking, reflection, process, and DPAPI APIs unless they are moved to a separate sandbox/capability runtime.
- DPAPI does not protect saved Orbit passwords from a malicious script/plugin running under the same Windows user. Do not market saved credentials as protected from untrusted community code.

## Multi-Session Injection Notes

For launcher mode, Orbit now protects against common multi-launch issues by:

- serializing launcher starts briefly so each process captures the intended `JX_*` values
- avoiding duplicate `rs2client` PID binding across sessions
- refusing injection when another active session already owns that PID

If only one session injects correctly, enable logs and verify PID/account mapping per launch.

## Injector DLL Selection

Orbit supports selecting a custom injector DLL from **Settings -> General -> Injector DLL**.

- `Browse` to a DLL path
- pick from recent DLLs in the dropdown
- `Use Default` to revert to built-in `XInput1_4_inject.dll` behavior

Orbit shows file metadata (version/product/description when available) and a best-effort MESharp compatibility marker.

## Logging and Diagnostics

Orbit has dedicated diagnostics in **Settings -> Advanced**:

- `Enable theme debug logging`
- `Enable Orbit interaction logging (drag/drop/reparent)`
- `Enable MESharp integration (runtime/script commands)`

Logs are written under Orbit's local logs path (relative to app/runtime storage). Use the built-in **Open log file** actions in Settings for direct access.

When MESharp integration is disabled, Orbit still supports launcher/session management and DLL injection, but MESharp-specific runtime and script command features are intentionally disabled.

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
- `InjectorPathResolver`: injector DLL and runtime asset validation
- `OrbitLayoutStateService`: shared layout state for Orbit View
- `ScriptManagerService`: script load/reload coordination
- `AccountService`: account persistence with DPAPI credential protection
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
