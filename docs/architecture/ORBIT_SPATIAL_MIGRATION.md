# Orbit Spatial Workbench Migration Plan

> **Status:** Active long-horizon migration plan  
> **Target experience:** Orbit Spatial / Spatial Workbench  
> **Implementation strategy:** Incremental, behavior-preserving shell migration  
> **Figma reference:** `Orbit-Fluent-Mockup`, pages `00 — Spatial Workbench Direction` through `04 — Orbiter Interaction States`

## 1. Why this document exists

Orbit Spatial is not a reskin. It changes the shell interaction model while preserving Orbit's runtime, session, scripting, plugin, and embedded-client capabilities.

The migration will span many changes and may involve different contributors or coding agents. This document is the durable source of truth for the intended architecture, sequencing, design invariants, compatibility rules, and completion criteria. Contributors should update this plan when a material decision changes rather than allowing the implementation and design intent to drift apart.

The key product decision is:

> **The Orbiter is the menu. There must not be a second persistent application-navigation system competing with it.**

Orbit Spatial may use tabs, docking, inspectors, toolbars, and status surfaces, but none of those should become a redundant global navigation rail, sidebar, or permanent global search box.

## 2. Experience modes

Orbit is expected to support multiple shell presentations over shared application services and view models.

### Orbit Classic

- Closest to the current application.
- Floating-first and expressive.
- Existing Orbiter behavior and current shell composition remain available.
- Serves as the compatibility and rollback path during migration.

### Orbit Fluent

- Conventional Windows utility presentation.
- Persistent navigation and page-first information architecture are acceptable in this mode.
- Inspired by the PowerToys mockup.
- This mode is intentionally distinct from Spatial and must not define Spatial's navigation model.

### Orbit Spatial

- Orbiter-first, dockable workbench.
- No permanent global navigation rail or sidebar.
- No permanent global search box.
- Tabs remember open work.
- Docking organizes simultaneous work.
- Contextual commands and inspectors appear only after selection and may overlay, dock, float, or dismiss.

All modes should share the same session, tool, script, runtime, plugin, logging, and theme services. Modes change shell composition and defaults, not application truth.

## 3. Non-negotiable design invariants

These rules are architectural guardrails, not optional visual preferences.

### 3.1 One owner for global discovery

The Orbiter owns:

- application-wide destinations;
- tool and session discovery;
- launch actions;
- recent work;
- global commands;
- command search.

Do not add a persistent rail, sidebar, hamburger menu, or title-bar search box to Orbit Spatial.

### 3.2 Tabs own persistence

Once work is opened, it remains represented by a dockable tab. Switching among open work should not require reopening the Orbiter.

### 3.3 Docking owns composition

Splitting, stacking, tearing off, and arranging tools or sessions remain responsibilities of Dragablz/Dockablz and the shell workspace. Do not create page-specific bespoke split systems that compete with the existing workbench.

### 3.4 Context earns space

Selection-specific commands and details may appear as:

- a transient popup inspector;
- a docked inspector tab or pane;
- a floating tool window;
- an inline detail region when the task genuinely benefits from it.

A permanent right inspector must not become mandatory shell furniture. In Orbit View especially, client workspace should remain primary.

### 3.5 Context replaces; it does not duplicate

When an object is selected, the Orbiter may temporarily enter Context mode and replace global destinations with object-specific commands. Global and contextual menus must not remain open as two competing persistent systems.

### 3.6 Shared operational core

The migration must preserve and reuse existing services for:

- session lifecycle and reconciliation;
- session placement and ownership;
- script orchestration;
- MESharp integration;
- console capture;
- plugins and tools;
- Dragablz tear-off and rehoming;
- embedded client resize and focus behavior.

A shell rewrite is not permission to reimplement stable runtime logic inside new view models.

### 3.7 Classic remains viable until Spatial is hardened

Spatial must be introduced behind an explicit shell-mode setting. Classic remains available until the Spatial exit criteria and compatibility matrix are satisfied.

## 4. Current architecture baseline

The current codebase already contains much of the difficult mechanical foundation:

- `MainWindow.xaml` hosts a `Dockablz.Layout` with `TabablzControl` branches.
- `InterTabClient` supports tear-off windows and distinguishes main-shell and Orbit View origins.
- `MainWindowViewModel` owns open tabs, selected sessions, tool commands, and floating-menu state.
- `FloatingMenuGeometryService`, `FloatingMenuVisibilityService`, and `FloatingMenuQuickToggleService` implement positioning, visibility, snapping, and quick-toggle behavior.
- `ThemeService` already publishes semantic surface, text, border, action, and status resources.
- `ChildClientView` embeds the native RuneScape window through `WindowsFormsHost`.

The primary structural risk is concentration: `MainWindow.xaml` and `MainWindowViewModel` currently combine workbench, Orbiter, shell chrome, native-client coordination, and application command concerns. Spatial should be enabled by decomposing this shell rather than adding another large conditional layer to it.

## 5. Target shell architecture

The target composition is conceptually:

```text
MainWindow
├── OrbitTitleBar
├── WorkspaceTabStrip / Dragablz shell chrome
├── WorkspaceHost
│   └── Dockablz.Layout
├── StatusStrip
├── OrbiterHost
└── TransientSurfaceHost
    ├── ContextInspector
    ├── Notifications
    └── Teaching / migration affordances
```

The exact WPF control tree may differ where Dragablz templates require it, but ownership boundaries should remain clear.

### 5.1 `OrbitWorkspaceHost`

Responsibilities:

- own the primary `Dockablz.Layout` and `TabablzControl` presentation;
- retain existing bindings to `Tabs`, `SelectedTab`, and `InterTabClient`;
- expose shell events through commands or narrow callbacks;
- avoid knowing about Orbiter presentation;
- preserve branch collapse, tear-off, and native client resize behavior.

### 5.2 `OrbiterHost`

Responsibilities:

- host the draggable/snap-capable Orbiter handle;
- preserve Popup-based rendering above embedded HWND content;
- render state-specific Orbiter surfaces;
- delegate command discovery and state transitions to services;
- avoid hardcoding application commands as a long static XAML button list.

### 5.3 `TransientSurfaceHost`

Responsibilities:

- display inspectors and transient contextual UI;
- select an airspace-safe host strategy;
- support overlay, dock, float, and dismiss transitions;
- never steal permanent workspace by default.

### 5.4 `OrbitStatusStrip`

Responsibilities:

- present concise runtime health and workspace status;
- avoid becoming another navigation bar;
- expose diagnostic detail through normal tool tabs or the Orbiter.

## 6. Orbiter interaction model

The Orbiter has four primary modes.

### Dormant

- Present and discoverable.
- Quiet and compact.
- Consumes no reserved layout space.
- May be edge-magnetized to yield to active controls.

### Global Navigation

- The sole application-wide navigation surface in Spatial.
- Shows destinations, launch actions, recent work, and global commands.
- May include a command-entry field inside the summoned surface.

### Command Lens

- Entered by typing, hotkey, or explicit search action.
- Searches commands, tools, sessions, settings, and already-open tabs.
- Results execute commands or focus/open work; this is not web-style content search.

### Context

- Entered when a session, script, log event, theme, tool, or other supported object becomes selected.
- Replaces global destinations with object-specific commands.
- Back, deselect, or center-click returns to Global Navigation.
- Dismiss may return to Dormant while retaining selection context internally.

The first implementation should keep state transitions deterministic and independently testable before wiring new visuals.

## 7. Command architecture

The existing Orbiter menu hardcodes individual buttons and visibility properties. Spatial should converge on a command registry.

A future descriptor should carry, at minimum:

```csharp
public sealed record OrbitCommandDescriptor(
    string Id,
    string Title,
    string? Subtitle,
    object? Icon,
    ICommand Command,
    string? GestureText,
    OrbitCommandCategory Category,
    Func<bool>? CanDisplay = null);
```

The same descriptors should be consumable by:

- Global Navigation;
- Command Lens;
- Context mode;
- keyboard shortcuts;
- conventional menus in Orbit Fluent;
- accessibility alternatives;
- selected toolbars where appropriate.

Command registration must not move runtime logic into the descriptor. Descriptors adapt existing commands and services.

## 8. Context architecture

A context object should identify the selected entity without forcing the shell to know every domain type.

Target shape:

```csharp
public interface IOrbitContext
{
    string Kind { get; }
    string DisplayName { get; }
    object? Source { get; }
    IReadOnlyList<OrbitCommandDescriptor> Commands { get; }
    object? InspectorViewModel { get; }
}
```

Expected contexts include:

- session;
- script;
- console event;
- theme;
- tool;
- plugin;
- runtime service or health item.

Context providers may adapt domain objects into this interface. Domain models should not be forced to reference shell presentation types.

## 9. WPF airspace strategy

`WindowsFormsHost` creates a native child HWND. Ordinary WPF visuals cannot reliably overlay that region.

Use the following rule:

| Surface | Preferred host |
|---|---|
| Orbiter and expanded Orbiter over a client | WPF `Popup` or owned transparent window |
| Transient inspector over a client | WPF `Popup` or owned tool window |
| Inspector docked beside a client | Normal WPF pane within the layout |
| Notifications that may cross client content | Popup/owned window |
| Cell chrome outside the hosted HWND | Normal WPF |

Every new transient surface must be tested over a live embedded client, not only against design-time placeholders.

## 10. Theme architecture v2

The current semantic resource system is a strong base, but Theme Studio proposes a broader contract.

A future `ThemeDefinitionV2` should cover:

- base mode;
- shell mode preference;
- semantic color tokens;
- typography;
- density;
- spacing;
- shape/corner radii;
- motion and reduced-motion behavior;
- Orbiter glow, opacity, and flyout treatment;
- high-contrast fallback metadata.

Theme preview must use a scoped `ResourceDictionary` so edits can be staged without repainting the live application until Apply is chosen.

Existing custom themes require migration with sensible defaults. Theme files need a schema version and forward-compatible extension strategy.

## 11. Migration phases

### Phase 0 — plan and state contracts

**Goal:** Preserve intent and introduce testable shell-state primitives without changing the visible UI.

Deliverables:

- this migration plan;
- `OrbitShellMode` contract;
- deterministic Orbiter mode/state service;
- unit tests for the state transition contract.

Exit criteria:

- no visible behavior change;
- tests cover summon, dismiss, command-lens, context, and back transitions;
- later shell work can depend on the state service without depending on `MainWindow`.

### Phase 1 — behavior-preserving shell extraction

**Goal:** Decompose `MainWindow` without intentional visual or interaction changes.

Deliverables:

- extract `OrbitWorkspaceHost`;
- extract existing Orbiter XAML into `OrbiterHost`;
- extract shell status and title/tab chrome where practical;
- move narrowly scoped code-behind behavior into attached behaviors, services, or control code-behind;
- maintain existing bindings and settings compatibility.

Exit criteria:

- Classic looks and behaves the same within accepted rendering tolerance;
- session launch/injection/focus behavior is unchanged;
- splits, tear-off, rehome, and empty-branch collapse pass manual testing;
- Orbiter dragging, snapping, inactivity, and quick-toggle behavior pass existing and new tests;
- live embedded-client resize and focus pass manual testing.

### Phase 2 — shell mode service and Spatial chrome

**Goal:** Add Spatial as an opt-in shell composition while preserving Classic.

Deliverables:

- `ShellModeService` with persisted `Classic`, `Fluent`, and `Spatial` values;
- restart-required mode switching initially;
- Spatial title bar, compact tab strip, and status strip;
- no rail, sidebar, or global title-bar search;
- mode-aware shell resource dictionaries.

Exit criteria:

- switching mode and restarting selects the correct shell;
- Classic remains unchanged;
- open/close/tear-off behavior is equivalent in Spatial;
- no application-wide destination is available only through hidden legacy UI.

### Phase 3 — Orbiter 2.0 global navigation

**Goal:** Make the Orbiter the complete global menu.

Deliverables:

- command registry and descriptors;
- Global Navigation surface;
- recent work and open-tab focusing;
- keyboard summon behavior;
- migration of existing hardcoded Orbiter actions to registered commands;
- accessible text labels and keyboard traversal.

Exit criteria:

- all existing global Orbiter actions remain reachable;
- no persistent duplicate global navigation exists in Spatial;
- commands respect `CanExecute` and availability changes;
- global navigation can be used without a mouse.

### Phase 4 — Command Lens

**Goal:** Add command-first discovery without adding permanent search chrome.

Deliverables:

- searchable command index;
- session, tool, setting, and open-tab providers;
- ranking for exact, prefix, token, recent, and contextual matches;
- explicit empty/error/loading states;
- command telemetry/logging limited to local diagnostic events unless otherwise approved.

Exit criteria:

- typing can summon and filter the lens;
- keyboard selection and execution are reliable;
- queries do not block the UI thread;
- command execution is routed through existing services and commands.

### Phase 5 — contextual Orbiter and transient inspector

**Goal:** Make selection context useful without permanently reducing workspace.

Deliverables:

- context provider contracts;
- session context first;
- context-specific Orbiter commands;
- transient inspector host using an airspace-safe surface;
- dock, float, and dismiss transitions;
- back/deselect behavior.

Exit criteria:

- selected session commands mirror existing behavior;
- inspector renders above a live `WindowsFormsHost` client;
- closing or changing context cannot leave stale commands active;
- no context action can accidentally target a previously selected session.

### Phase 6 — Activity Hub

**Goal:** Add an operational summary as a normal pinned workspace tab.

Deliverables:

- aggregate existing session, injection, script, health, and console information;
- open/focus selected sessions;
- surface attention states without inventing new runtime truth;
- startup/pinning preference.

Exit criteria:

- Activity Hub owns no duplicate session lifecycle state;
- data refresh is event-driven where practical;
- closing Activity Hub leaves a valid shell;
- the hub remains usable at compact window sizes and high DPI.

### Phase 7 — Orbit View Spatial treatment

**Goal:** Maximize live-client workspace while adding optional context tools.

Deliverables:

- updated command strip and cell chrome;
- selected-cell context integration;
- transient inspector;
- Orbiter edge-yield/magnetization behavior;
- density-aware 2×2, 3×3, and custom layouts.

Exit criteria:

- live clients remain the dominant visual surface;
- no persistent inspector reduces grid area by default;
- drag/drop, tear-off, rehome, resize, and focus behavior remain correct;
- controls remain usable at 100%, 125%, 150%, and 200% scaling.

### Phase 8 — Theme Studio and schema v2

**Goal:** Make the proposed theming capabilities real.

Deliverables:

- versioned theme schema;
- semantic token editor;
- scoped live preview;
- density, shape, motion, and Orbiter token editing;
- import/export and migration;
- contrast checks and high-contrast behavior.

Exit criteria:

- legacy themes migrate without data loss;
- preview changes do not mutate the live app until Apply;
- every Spatial shell surface consumes semantic resources;
- theme application is reversible and failure-safe.

### Phase 9 — hardening and default-mode decision

**Goal:** Decide whether Spatial is ready to become the default.

Validation matrix:

- single and multi-monitor;
- mixed-DPI monitors;
- window maximize/restore/minimize;
- native client focus and keyboard input;
- Dragablz split and tear-off;
- Orbiter placement on every edge and corner;
- reduced motion;
- Windows high contrast;
- keyboard-only navigation;
- screen-reader naming for primary commands;
- corrupted settings and theme recovery;
- plugin-provided tools;
- update and shutdown flows.

Spatial becomes the default only through an explicit product decision after this matrix passes. Classic removal, if ever considered, requires a separate decision and migration plan.

## 12. First implementation slice on this branch

This branch begins Phase 0 only.

Included:

- `OrbitShellMode` enum;
- `OrbiterMode` enum;
- lightweight `OrbiterContextSnapshot` contract;
- `OrbiterStateService` with deterministic transitions;
- unit tests for the transition contract.

Explicitly not included yet:

- visible shell changes;
- replacement of the current floating-menu XAML;
- command registry;
- context providers;
- shell-mode persistence;
- Activity Hub;
- Theme Studio schema changes.

This boundary is intentional. It creates a stable seam before touching `MainWindow` and keeps the first code slice easy to validate and revert.

## 13. Compatibility and rollback

- Existing settings keys must not be repurposed with incompatible meanings.
- New settings need defaults that preserve Classic behavior.
- Theme migration must copy/upgrade data rather than destructively overwrite legacy definitions.
- Spatial components should consume existing runtime services rather than fork them.
- Every phase should be independently revertible without requiring runtime data migration, except the eventual versioned theme schema, which must include a downgrade-safe backup/export path.

## 14. Testing strategy

### Unit tests

Prioritize pure services and state transitions:

- Orbiter state machine;
- command ranking;
- context replacement and stale-context prevention;
- shell-mode parsing and persistence;
- theme migration;
- geometry and snap behavior.

### WPF integration tests

Use narrowly where practical for:

- resource dictionary selection;
- command `CanExecute` updates;
- view-model/control binding contracts;
- shell-mode composition.

### Manual native-client matrix

Required because automated WPF tests do not adequately validate HWND airspace, focus, and mixed-DPI behavior.

Record the tested scenarios in PR descriptions for phases touching:

- `WindowsFormsHost`;
- Popups or owned transparent windows;
- window placement;
- Dragablz tear-off;
- multi-monitor DPI.

## 15. Performance and reliability guardrails

- Do not perform command indexing or filtering synchronously over expensive providers on the UI thread.
- Avoid continuously animating blurred effects over embedded clients.
- Virtualize large command, log, session, and theme lists.
- Dispose event subscriptions from shell-scoped services and transient windows.
- Keep Popup ownership and shutdown behavior explicit to avoid orphan windows.
- Treat selection changes as versioned/replaceable context so delayed async work cannot execute against stale objects.
- Preserve current session ownership and single-host invariants.

## 16. Accessibility guardrails

- Every icon-only command requires an automation name and tooltip.
- Global Navigation and Command Lens must be fully keyboard operable.
- Focus must return predictably when the Orbiter or inspector dismisses.
- Reduced-motion settings must disable nonessential glow, pulse, and scale animations.
- High-contrast mode must use system or explicit high-contrast resources rather than relying on translucent surfaces.
- Text sizing and density must not assume a fixed 100% display scale.

## 17. Decision log

Update this section when an architectural decision changes.

| Date | Decision | Rationale |
|---|---|---|
| 2026-07-15 | Name the third experience mode `Orbit Spatial` and the design language `Spatial Workbench`. | Distinguishes the Orbiter-first workbench from the conventional PowerToys-inspired Fluent shell. |
| 2026-07-15 | The Orbiter remains the sole global menu in Spatial. | A rail or sidebar would duplicate the purpose of the current floating menu. |
| 2026-07-15 | Preserve Dragablz/Dockablz as the workspace composition system. | Orbit already has mature split, tear-off, and rehome behavior. |
| 2026-07-15 | Use Popup/owned-window surfaces over embedded clients. | `WindowsFormsHost` airspace prevents reliable ordinary WPF overlays. |
| 2026-07-15 | Begin with pure state contracts before shell extraction. | Deterministic state and tests create a safe seam before modifying the large current shell. |

## 18. Definition of complete

The migration is complete when:

- Spatial has no redundant persistent global navigation;
- the Orbiter fully supports Global Navigation, Command Lens, and Context modes;
- tabs and docking preserve all current workspace capabilities;
- inspectors can overlay, dock, float, and dismiss safely over live clients;
- Activity Hub reflects existing runtime truth;
- Theme Studio edits a versioned semantic theme model with scoped preview;
- Classic remains available until a separate decision removes it;
- the hardening matrix passes;
- this document accurately reflects the shipped architecture.
