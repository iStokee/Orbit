# Green Dragon Node Sample (from Stokee DragonSlayer)

This sample mirrors the working `GreenMachine` FSM from `API_examples/StokeeDragonSlayer`. It focuses on the logic flow, not UI. Load `green-dragon-node-example.orbitfsm.json` into the node editor to inspect.

## Signals used
- `atEdge`, `atBank`, `needsBank`, `atWall`, `wallCrossed`, `atDragons`
- `inventoryFull`, `hasNotepaper`
- `clickedDragon`, `inCombat`, `randomsFound`

## Node mapping (high level)
- Start → Check location (sets location signals based on player coords/inventory).
- Teleport → Run to bank → Banking → Teleport → Run to wall → Click wall → Run to dragons.
- At dragons: Check inventory → Notepaper/teleport if full → Find dragon → Check combat → Loot → Randoms/Flee → Teleport out or loop.

## Executor expectations
- Action nodes should emit the signals above after performing their work (e.g., location check sets `atEdge/atBank/needsBank/atWall/atDragons`; loot sets `inventoryFull` and `randomsFound`; combat loop updates `inCombat`).
- Interaction/Traversal executors should update location signals when arriving (e.g., `atWall`, `atDragons`).
- Boolean condition nodes already read from the signal map.

Tip: start by wiring `Generic Action`/`Interaction`/`Walk` executors to update the signal map; the flow will then follow the same transitions as the original FSM.***
