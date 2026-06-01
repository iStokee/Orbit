# Orbit Workbench Console UI Style Guide

Orbit uses the **Orbit Workbench Console** style: a desktop power-user interface for supervising RuneScape sessions, scripts, runtime state, logs, and plugins. It should feel closer to an IDE, operator console, or admin workbench than a consumer dashboard.

## Style Contract

### Shell

- The main shell is a tabbed and dockable workbench using Dragablz.
- Primary work happens in tool tabs, session tabs, and Orbit View grid cells.
- Global actions belong in the main shell or a tool command bar, not scattered into unrelated panels.
- Tool pages should use the same structure: page root, header, command bar when needed, then workbench panels.

### Visual Language

- Use MahApps and Orbit resource keys instead of hard-coded colors.
- Prefer dark-first, theme-aware surfaces.
- Use restrained accent color for selected state, primary actions, and important status, not for every panel.
- Controls are moderately rounded: usually 4px for small controls, 6-8px for panels/badges, 10px only for prominent headers.
- Avoid nested decorative cards. Use panels for grouped controls, cards only for repeated objects such as sessions, scripts, plugins, accounts, or tools.
- Avoid large marketing-style hero layouts. Orbit is a work surface.

### Layout

- Page root: `Orbit.Workbench.PageRoot`.
- Tool header: `Orbit.ToolHeader` with `Orbit.ToolHeaderIcon`, `Orbit.ToolHeaderTitle`, `Orbit.ToolHeaderSubtitle`, and optional meta/status on the right.
- Command strip: `Orbit.Workbench.CommandBar`.
- Grouped content: `Orbit.Workbench.Panel`.
- Selectable repeated content: `Orbit.Workbench.SelectableCard`.
- Lists should stretch horizontally and avoid arbitrary fixed widths unless the control is fixed-format.
- Dense operational tables and logs are acceptable; preserve scanability over whitespace.

### Spacing

- Default outer page margin is `Orbit.ToolPageMargin`.
- Standard panel padding is 12px.
- Standard vertical separation between header, command bar, and content is 10px.
- Small inline gaps should be 4px, 6px, or 8px.
- Do not introduce one-off 15/17/22px spacing unless there is a layout-specific reason.

### Typography

- Use Segoe UI through the shared app resources.
- Body text defaults to 12px.
- Page/tool titles use `Orbit.ToolHeaderTitle`.
- Panel titles should use `Orbit.Workbench.SectionTitle`.
- Helper text should use `Orbit.Workbench.HelperText`.
- Status/detail text should use `Orbit.Workbench.MetaText`.
- Do not use hero-scale type inside panels, settings pages, dialogs, or dense tools.

### Commands

- Primary action: `Orbit.Workbench.Button.Primary`.
- Normal command: `Orbit.Workbench.Button`.
- Icon-only compact action: `Orbit.Workbench.IconButton`.
- Prefer icons plus labels for important commands, especially load/reload/add/open/start/stop.
- Use toggles for binary runtime policy and segmented toggle buttons for compact mode choices.

### Status

- Status belongs in badges, small state dots, log rows, or the right side of a header/command bar.
- Use `Orbit.Workbench.Badge*` styles for compact categorical status.
- Keep runtime status visible but quiet unless it is an error or destructive state.

### Surface Guidance

- **Orbit View**: mission-control workspace. It can be denser and more command-heavy than other tools because it is the core multi-session canvas.
- **Settings**: inspector/workbench form. Group settings into clear panels, keep controls aligned, and avoid isolated full-width decorative sections.
- **Console**: operator log viewer. Dense filters, clear source/level/status, fixed command bar, readable log surface.
- **Script/Plugin/Tool Managers**: workbench browser pattern. Header, command bar/filter row, list/grid of repeated objects, details/diagnostics panel when useful.
- **Dialogs and secondary windows**: compact workbench form. Use the same resources but reduce decoration.

## Refactor Checklist

When touching a view:

- Replace local hard-coded page margins with `Orbit.Workbench.PageRoot` where practical.
- Replace one-off section boxes with `Orbit.Workbench.Panel`.
- Replace repeated object containers with `Orbit.Workbench.SelectableCard`.
- Replace local title/helper/meta text styling with shared workbench text styles.
- Keep existing commands and bindings intact.
- Preserve theme awareness by using `Orbit.Brushes.*` resources.
- Check light/dark contrast when adding new foreground/background combinations.

