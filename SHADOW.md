# Shadow world and batch validator

## Why

The bot generates N command streams. Generation is nearly free (the second stream costs
<20% throughput), but picking the best stream currently costs a second LLM round — 6x
instead of 3x, and that extra second is what kills the whole thing. The validator replaces
the LLM arbiter with code: run every batch through the shadow world, drop the ones that
cannot execute, execute the survivor. Best-of-N stays, the arbitration round disappears.

It also removes a class of errors that today cost a network round-trip: a block placed on
the engineer's own coordinates, an occupied cell, missing components, a block type that
does not exist.

## Principle

A command is written **once**. It sees the world through interfaces and applies effects
through an effector. Two implementations:

* **R** — `RealWorld`, passthrough to the game, behaviour exactly as today.
* **T** — `ShadowWorld`, an overlay on top of R: reads lazily through R, writes into its
  own dictionary of changes.

`EQS`, `AStarHelper`, `ConveyorAStar` and `Draft` take a whole `IMyCubeGrid` and are not
routed through the layer. `IGridView.Grid` hands them the real grid — so in the shadow they
see the unmodified world, and any command reaching them after a shadow mutation must report
Unknown.

In T a command completes instantly: Fly does not fly, it moves the coordinate; Weld does not
weld, it raises integrity to full. Loop exit conditions are read through the layer, so the
loop exits on its first iteration.

**Cells, not objects.** The shadow can say what stands in a cell; it cannot produce the
`IMySlimBlock` for a block it has only predicted. So occupancy and adjacency go through
`IGridView.CellDefinition`, and `GetCubeBlock` on a predicted cell is Unknown. This is what
lets a batch place several blocks against each other — the case the validator exists for.

**Unknown.** The shadow is allowed not to know. Removing a block can split a grid, and the
engine will not compute that for us. Such a read returns Unknown, batch validation stops
there, and the batch score is the length of the validated prefix. Stopping at the fifth
command beats stopping at the first.

## Hard rules

* Unknown = pass. Reject only on certainty: a false rejection costs more than a miss,
  because a miss is caught by the runtime error path, which already works.
* Iteration cap in T. Otherwise an uncovered exit condition hangs the game.
* No command implemented twice. If one appears, the architecture is broken.

---

## Plan

### 1. Interfaces

- [x] `IGridView` — member names identical to `IMyCubeGrid` so that the 77 uses of
      `selectedGrid` compile unchanged (`World.cs`)
- [x] `IWorld` — engineer, block state, inventory reads, pacing and all effects in one
      interface; separate view interfaces turned out to be needless, the shadow state is
      one object (`World.cs`)
- [x] Block state (`Integrity`, `IsDestroyed`, `StockpileEmpty`) reads through `IWorld`:
      commands hold `IMySlimBlock` handles, so the shadow cannot move that state otherwise
- [x] Pacing (`SetPause`, `IsPaused`, `ToolReady`) in `IWorld`: the shadow answers "over
      already", or every wait loop burns the iteration cap
- [x] Effects are semantic (`Move`, `ToolShoot`, `PlaceBlock`), not native
      (`MoveAndRotate`, `Shoot`) — a shadow cannot answer the native ones without physics

### 2. Passthrough (R)

- [x] `RealGrid` — `IGridView` passthrough to the game
- [x] `RealWorld` — `IWorld` adapter over the existing `Commands` methods
- [x] `IWorld.View(grid)` hands out the grid view, so the world has a single entry point
      while `selectedGrid` keeps its 77 call sites
- [x] `Commands.world`, set to `RealWorld` in the constructor

### 3a. Move commands onto R — first slice

- [x] `selectedGrid` is an `IGridView`; 36 pass-through uses spell `selectedGrid.Grid` and
      mark exactly where the shadow will be blind
- [x] `GetEngineerCenter` reads from the world (`RealWorld` computes it from the character —
      routing it back through `Commands` would recurse)
- [x] Block placement through `IWorld.PlaceBlock`
- [x] Silent `as` casts hunted down: eight `selectedGrid as MyCubeGrid` / `as MyEntity`
      compiled fine against the interface and would have returned null at runtime
- [x] Builds

### 4. Test: the game still works

Run this before the second slice, not after — the null-cast class of failure is cheaper to
find now than under thirty more edits.

- [x] `place`, `weld`, `grind` in game — behaviour indistinguishable from today

### 3b. Move commands onto R — the rest

- [x] **Grep for silent casts and comparisons after every type change here.** Six comparisons
      of `selectedGrid` against an `IMyCubeGrid` compiled and were always false: `inventories`
      and `overview` listed nothing, the draft thought it belonged to another grid
- [x] Block state (`Integrity`, `IsDestroyed`, `StockpileEmpty`) through `IWorld`.
      `MaxIntegrity` stays on the handle — it comes from the definition and never moves
- [x] Inventory reads and `TransferItemTo` through `IWorld`. `InventoryTransfer` and
      `InventoryDelta` stopped being static to reach the world
- [x] Movement: `CharacterMove` / `CharacterRotateTo` call sites onto `IWorld.Move` / `RotateTo`
- [x] A whole flight is one effect: `IWorld.FlyTo` wraps `RealFly`, the shadow arrives at once
- [x] Wait loops onto `IWorld.IsPaused`; the tool onto `ToolEquipped` / `ToolReady` /
      `ToolShoot` / `ToolStop` / `EquipTool` — the gun object is gone from the commands
- [x] `RazeBlock` and the grinder path
- [x] Real effects found while sweeping and routed so the shadow cannot fire them for real:
      `MoveItemsToConstructionStockpile`, `AttachPilot`, `RemovePilot`
- [x] Leave `WorldToGridInteger` / `GridIntegerToWorld` alone — pure transforms
- [ ] Second in-game run: `place`, `weld`, `grind`, `fly`, `approach`, `get`/`put`, `enter`,
      `inventories`, `overview`, `draft`/`build` — **do not start the shadow before this box**

### 5. Shadow (T)

`ShadowWorld.cs`. Written and building; nothing drives it yet, and the commands that read
block state off the handle still bypass it — that is stage 3b.

- [x] State: engineer position, placed and removed cells, inventory amounts, block integrity
- [x] Lazy reads through R, writes into the overlay
- [x] Instant execution: pauses are over on the spot, the tool is always ready, welding
      fills integrity and grinding empties it in one shot
- [x] Iteration cap → Unknown (`ShadowWorld.Step`, called by the runner in stage 6)
- [x] Unknown rules: once a grid's overlay is non-empty, `Grid` and `GetBlocks` are Unknown —
      which covers EQS, AStar, conveyor reachability and grid split in one place
- [x] `Commands.Draft.cs` keeps planned blocks and a preview grid, not an overlay with undo
      over an existing grid — nothing to reuse, `ShadowGrid` holds the same shape itself
- [x] Inventory transfers address slots by index; the second transfer out of one inventory
      is Unknown. Enough for take-then-weld, not for a repacking batch
- [ ] Not routed on purpose: the `inventories` and `overview` listings read the rich item
      list off the game inventory, so after a predicted transfer they print stale contents.
      Output only — nothing decides on it

### 6. T against R

- [ ] Every command that really executes runs through T first, then through R
- [ ] Divergences are logged: what the shadow predicted, what actually happened
- [ ] Keep running in this mode until the log stays empty over a normal session
- [ ] The list of divergences is the task list for this stage

### 7. Prediction: the experiment itself

- [ ] Selector in `LLM.cs`: N batches through T, score = (valid prefix length, error
      count), the winner executes
- [ ] Remove the LLM arbitration round
- [ ] Measure against the current 6x: errors per batch, time to the first executed
      command, how smart the bot feels

---

## Side effects to watch for

* The shadow cuts long batches more often than short ones — the bot gets dumber by
  choosing the timid option. Symptom: mean length of executed batches drops.
* The iteration cap fires often — that means an uncovered exit condition, not a bad command.
* Unknown is too eager — prefixes are short and the selector picks almost at random.
* T and R diverge silently in a rare case that never surfaced at stage 6.
