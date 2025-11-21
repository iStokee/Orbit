# Orbit Visual Node Editor Plan

Context: Orbit already ships a WPF FSM node editor (Start/Action/Condition/Terminal nodes, manual transitions, boolean signals, file persistence). We want to evolve it toward a VisualRM-like node builder with a node catalog, typed parameters, richer UI, and C# API-backed execution.

## Goals
- Present a node catalog (categories + definitions) similar to VisualRM (Bot states, Checkpoints, Conditions, Actions, Modulators, Convertors, Miscellaneous).
- Let users drag/drop definition cards to create nodes on the canvas.
- Edit nodes via typed parameter forms (per definition) instead of only free text.
- Preserve FSM transitions but allow node execution results to drive branching.
- Bind node execution to the C# API through pluggable handlers.
- Keep JSON persistence shareable and versioned; migrate legacy FSMs.

## Data Model (proposed)
- `NodeCategory { Id, Title, Description, Icon, Order, Slug }`
- `NodeDefinition { Id, Title, ShortDescription, Icon, CategoryId, Order, HasQuery, Parameters: List<NodeParam> }`
- `NodeParam { Key, Label, Type (string|number|bool|enum|list|coordinate|entity|item|gameobject|npc|area), IsRequired, AllowMultiple, AllowPartial, EnumValues?, Placeholder? }`
- Script node instance (replaces/extends `FsmNodeModel`):
  - `Id, DefinitionId, TitleOverride?, DescriptionOverride?, Position (X,Y), DwellMs, Parameters: Dictionary<string, object?>, Transitions: List<ScriptTransition>`
  - Transitional model unchanged for now: `FromId, ToId, Label, ConditionKey, ExpectedValue, IsFallback, IsActive`.
- Catalog loader/service to provide seed definitions and allow future external loading.

## Runtime Model
- `INodeExecutor` per `NodeDefinition.Id` to call the C# API with typed parameters.
- Execution result: `Status (Success/Fail/Retry)`, `Outputs` (signal map, optional typed outputs), `Telemetry`.
- Transition resolution: prefer explicit outputs/signals; fall back to existing ConditionKey/ExpectedValue for backward compatibility.

## UX Plan
- Palette: category tabs/grid with definition cards (title, shortDescription, icon, hasQuery badge). Drag/drop or click-to-add.
- Canvas: keep current drag nodes + curved connectors; node card shows definition title/icon, start badge, active highlight.
- Inspector:
  - Header: select/change definition; title override; dwell ms.
  - Parameters: dynamic form from `NodeParam` (text, number, bool, enum dropdown, multi-select, coordinate picker, entity/item picker stubs).
  - Transitions: current UI preserved (target node, condition key, expected value, fallback, label).
- Signals panel: list signals produced/consumed; allow manual override for testing.
- Persistence actions: new/save/load/export remain.

## Migration Strategy
- Add catalog models + seed data (mirror VisualRM categories/nodes; start with Actions set: Interaction, Shop, Worldhop, Traversal, Keyboard).
- Extend `FsmNodeModel` or introduce `ScriptNodeModel` with `DefinitionId` + `Parameters`.
- Provide serialization converters and version tag for scripts.
- Legacy migration: load old FSM nodes as a “Generic Action” definition, mapping description/actionText/dwell into parameters.

## Work Breakdown (phased)
### Phase 1: Data + Services
- Add catalog models and a `NodeCatalogService` that returns seeded categories/definitions.
- Add `ScriptNodeModel` with parameters + `DefinitionId`; keep transitions the same.
- Wire DI for catalog service.

### Phase 2: UI baseline
- Palette UI pulling categories/definitions; add node on click.
- Node inspector shows definition picker and basic parameter form types (string/number/bool/enum/list).
- Node cards display definition title/icon.
- Persist new fields to JSON; add schema version/migration shim.

### Phase 3: Parameter/editor depth
- Add support for query/multi-select/partial-match options (for Interaction, Shop, Traversal).
- Add coordinate/area input helper; stub entity/item pickers.
- Improve transitions editor: dropdown of available signal keys from node outputs.

### Phase 4: Runtime binding
- Define `INodeExecutor` interface and registry; stub executors for seeded nodes.
- Feed executor outputs into transition resolution; update runner to use executors instead of dwell-only.
- Add logging/telemetry hooks.

### Phase 5: UX polish & docs
- Icons/badges, empty states, error surfacing.
- Docs: how to add definitions, how to implement executors, migration notes.

## Notes / Open Decisions
- Where to store catalog: seed in code now; optional external JSON later.
- Parameter value typing: use discriminated DTO in JSON to preserve types.
- Backward compatibility: keep loading old FSM JSON; map to a generic node definition.

