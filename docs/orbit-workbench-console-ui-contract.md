# Orbit Workbench Console UI Contract

Orbit's UI target is **Orbit Workbench Console**: a compact desktop workbench for session orchestration, scripting, diagnostics, and tool management.

This is a custom style, distilled from IDE/workbench shells, Windows Fluent controls, and operator-console density. It is not a marketing dashboard style.

## Shell

- Primary shape: tabbed workbench with detachable tools and session surfaces.
- Main surfaces: sessions, scripts, tools, settings, console/logs, diagnostics, and Orbit workspace.
- Global actions stay in the shell or tool header; contextual actions stay beside the data they affect.
- Tool pages use the same structure: page margin, header, optional command bar, content panels, and empty/error states.

## Density

- Default density is compact desktop.
- Use `Orbit.ToolPageMargin` for outer page spacing.
- Prefer 8px spacing increments: 4, 8, 12, 16, 20.
- Avoid oversized hero layouts, decorative sections, and loose dashboard spacing.
- Lists, logs, and operational views may be dense, but must keep clear row grouping and readable status.

## Layout

- Tool root: `Orbit.Workbench.PageRoot`.
- Header: `Orbit.ToolHeader` with `Orbit.ToolHeaderIcon`, title, subtitle/meta.
- Command bar: `Orbit.Workbench.CommandBar`, placed directly below the header when needed.
- Content: `Orbit.ToolCard` or `Orbit.Workbench.Panel`.
- Repeated item cards: `Orbit.Workbench.SelectableCard` for clickable rows/cards.
- Empty states: `Orbit.Workbench.EmptyState`.

## Visual Language

- Controls are Fluent/MahApps-inspired, not custom per screen.
- Corner radius: 6-8px for panels/cards/chips; 4px for compact badges; circular only for icon-only action buttons.
- Borders are preferred over shadows. Use shadows sparingly and only on top-level headers/cards already covered by shared styles.
- Backgrounds must use `Orbit.Brushes.*` resources, not ad hoc colors.
- Accent color is reserved for primary actions, selected state, and important status, not every decorative surface.

## Controls

- Text buttons: workflow commands with clear labels.
- Icon buttons: compact repeated item actions; always provide a tooltip.
- Toggle groups: segmented choices such as density, filters, and layout modes.
- Badges/chips: use shared badge styles for counts, status, and metadata.
- Search/filter boxes sit in the command bar or top of the relevant panel.

## Migration Rules

- Do not redesign each view independently.
- Remove local styles when a shared `Orbit.*` style can express the same intent.
- When adding a new local style, first ask if it should become a shared primitive.
- Keep XAML changes behavior-neutral unless fixing an obvious layout bug.
- Preserve existing bindings, commands, and visibility rules.

## Alternate Styles

It is feasible to try alternate named styles, but only if each style is defined as a contract like this one. Examples:

- **Orbit Carbon Operator**: sharper corners, denser tables, stronger grid lines, fewer shadows.
- **Orbit Fluent Workbench**: softer Windows-style spacing, more rounded panels, more prominent command bars.
- **Orbit Metro Compact**: flatter, square-ish, typography-led, minimal depth.

Switching styles becomes realistic when views depend on shared resources. The goal of this pass is to make `Orbit Workbench Console` the first stable style contract, so later variants can be tested by changing shared resources instead of rewriting every view.
