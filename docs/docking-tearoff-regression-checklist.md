# Orbit Docking/Tear-Off Regression Checklist

Use this checklist after changes touching tab docking, tear-off, rehome, or close behavior.

## Setup

1. Start Orbit.
2. Create at least 2 RuneScape sessions.
3. Open these tools as tabs:
   - Orbit View
   - Console
   - MCP Control
   - Account Manager

## Core Dragablz Flows

1. Tear off a session tab into a new window.
   - Expected: new host window opens and tab stays functional.
2. Drag that tab back into the primary window.
   - Expected: no duplicate or blank tab headers remain.
3. Drag a session/tool tab to an edge of the primary individual-mode shell.
   - Expected: a split branch is created outside Orbit View and the tab remains functional.
4. Drag a split individual-mode session/tool tab back into the primary tab strip.
   - Expected: no duplicate or blank tab headers remain.
5. Close the only tab in a split individual-mode branch.
   - Expected: the empty branch collapses, the splitter disappears, and remaining tabs reclaim the space.
6. Tear off an Orbit View-origin tab, then tear off again from the tear-off window.
   - Expected: origin remains tracked; Orbit-origin cleanup still works.
7. Empty a tear-off host by dragging/closing its last tab.
   - Expected: host closes (or branch collapses) automatically.

## Orbit View Rehome/Workspace

1. Move a session from individual tabs into Orbit View.
   - Expected: session appears once in Orbit View and not in the main tab strip.
2. Move a session from an individual-mode split branch into Orbit View.
   - Expected: session appears once in Orbit View and is removed from the split branch.
3. Move a tool from an individual-mode split branch into Orbit View.
   - Expected: tool appears once in Orbit View and is removed from the split branch.
4. Move the same session back to individual tabs.
   - Expected: session appears once in main tabs and is removed from Orbit workspace.
5. Close Orbit View tab while workspace contains sessions/tools.
   - Expected: workspace items are restored into regular tabs cleanly.

## Tool Lifecycle Integrity

1. Tear/rehome MCP Control tab between windows.
   - Expected: panel continues working; runtime state is not disposed by `Unloaded`.
2. Tear/rehome Console tab.
   - Expected: new log entries still auto-scroll after each move.
3. Tear/rehome Account Manager tab.
   - Expected: password reset UI event flow still works after moves.

## Session Close Integrity

1. Close a healthy session from tab close button.
   - Expected: process exits; session removed from sessions/tabs/workspace.
2. Force a shutdown failure scenario (simulate blocked process close).
   - Expected: session is kept visible/tracked in UI (not orphaned silently).
   - Expected: warning is logged indicating manual recovery path.
3. Retry close after failure.
   - Expected: session eventually exits and is removed cleanly.

## Leak/Orphan Watch

1. Repeat tear-off/rehome cycles 10+ times across tools/sessions.
   - Expected: no steadily growing count of dead windows or stale tabs.
2. Monitor Orbit logs for repeated reconcile/tear-off errors.
   - Expected: no sustained exception spam.

## Diagnostics

1. Open Sessions Overview and click `Dump Diagnostics`.
   - Expected: Orbit log includes one `diagnostic-session` line per live session.
   - Expected: `UI Conflicts` remains 0 after ordinary tab moves settle.
2. During a deliberate drag or tear-off, click `Dump Diagnostics`.
   - Expected: transient ownership can appear while dragging, but should settle after release and the next reconcile pass.
3. After closing a session from Orbit View or Gallery, click `Dump Diagnostics`.
   - Expected: closed/removed sessions do not appear in the session diagnostics list.
   - Expected: Gallery no longer shows a tile for the closed session after collection refresh.
