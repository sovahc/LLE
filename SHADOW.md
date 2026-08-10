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
- [ ] `IInventoryView`: `GetItems`, `GetItemAmount` — take the exact types from the call
      sites, the mod mixes the ModAPI and Ingame variants
- [ ] `IEngineerView`: `GetEngineerCenter`, character matrix
- [ ] `IEffector`: movement, `TransferItem`, `RemoveBlock`, `RazeBlock`,
      `SwitchCubePlacer`, block placement, weld/grind step

### 2. Passthrough (R)

- [x] `RealGrid` — `IGridView` passthrough to the game
- [ ] The remaining three implementations, one to one into game calls
- [ ] The context a command takes its views and effector from

### 3. Move commands onto R

- [ ] Change the type of `selectedGrid`, return `GetEngineerCenter` from the context
- [ ] Inventory reads through `IInventoryView` (`Commands.Inventory.cs`,
      `Commands.Construction.cs`)
- [ ] Effects through the effector: `MoveAndRotate` ×4, `SwitchCubePlacer` ×5,
      `TransferItem` ×2, `RemoveBlock`, `RazeBlock`, `SetPosition`; check `Tools.cs` for
      tool actuation
- [ ] Leave `WorldToGridInteger` / `GridIntegerToWorld` alone — pure transforms

### 4. Test: the game still works

- [ ] Builds
- [ ] In-game run: flight, block placement, welding, taking from a container, conveyor
- [ ] Behaviour indistinguishable from today — **do not start the shadow before this box**

### 5. Shadow (T)

- [ ] State: engineer position, placed and removed cells, inventory amounts, block
      integrity and components
- [ ] Lazy reads through R, writes into the overlay
- [ ] Instant execution: the T effector applies the result immediately, no animation
- [ ] Iteration cap → Unknown
- [ ] Unknown rules: block removal ⇒ grid topology Unknown; conveyor reachability after a
      topology change ⇒ Unknown
- [ ] Check `Commands.Draft.cs` — if it already keeps a shadow set of blocks with undo,
      reuse it instead of writing the cell overlay from scratch

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
