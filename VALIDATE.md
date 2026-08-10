# Command validation

## Why

Every turn several streams answer the same question and the pick is currently arbitrary. Over one
69-turn session with three streams, 31 of 207 answers called no tool at all, and some of the rest
called something that could not run. Checking the **first command of every stream** lets code take
the stream that will actually do something this round.

That is the whole gain, and it costs one cheap read of the world. No shadow world, no simulated
effects, no batch scoring, no second LLM round. Those were tried and the measurement is in
SHADOW.md at commit `c079ff1`.

## Principle

Every command splits in two inside one body. Everything above `yield return Validated` checks;
everything below it changes the world. `Validate(call)` drives the command to that yield and stops;
`Update()` runs straight through it. The checks are therefore run twice — once to check, once for
real — and they are cheap enough for that.

Four rules.

**The check is not a copy.** There is one copy of the guards, the command's own, so a check and a
body cannot disagree. This is the rule the shadow could not keep.

**Nothing above `Validated` may change anything.** This is what the split costs: the guarantee moved
from "the check is separate code" to "the author put the marker in the right place". It is local and
visible at the top of the function, which the copy problem never was.

**A command with no `Validated` passes.** Stage 1 is not finished the day it starts, and an unchecked
command must never lose a vote to a checked one. That is exactly how the shadow ended up demoting
the commands it could not model.

**The check rejects, it never suggests.** No "did you mean", no alternative cell, no choosing a
target. The mod reports state; the model chooses the action.

## What the check covers

The guard header that already stands at the top of nearly every command, before any `yield`:

* grid selected — `GridIsSet`
* arguments parse — `call.Ijk` / `Ijk2` / `NeedIjk`
* projection preview restrictions — `CurrentGridIsProjection`
* a block is at the cell, or is not — `Error: no block at I J K`
* standing at the interaction point — `IsAtInteractionPoint`, `E_BAD_POINT`
* `place` not onto the cell you occupy, and not into an occupied cell
* the block type exists
* the item name is known and the target has an inventory

**Not** in the check: EQS, A*, conveyor routing. They answer "can I get there", they cost real
frame time, and not knowing that is allowed. A command whose only remaining question is the path
passes.

---

## Plan

### 1. A validation point in every command

- [x] `Commands.Validate(ToolCall)` returning null or the refusal; it drives the command, descending
      into nested coroutines, and stops at the first yield
- [x] `yield return Validated` between the guards and the first change to the world
- [x] The ten instant commands that change something became coroutines; the seventeen that only read
      stayed instant and are checked by being run
- [x] `EquipTool` split into `FindTool` (pure) and the switch itself, so grind and weld can validate
      the tool and the interaction point without equipping anything
- [x] Builds

### 2. Streams and the pick

- [x] One request fanned out to every configured channel, all of them waited out
- [x] Only stream 0 prints to the console; the log keeps all of them
- [x] Score of a stream: first command runs > first command is refused > the answer called no tool.
      Ties go to the lowest channel index. A command with no `Validated` scores as running, which is
      the rule above
- [x] Losing answers are dropped whole — the transcript keeps one voice, and the model is never
      told it was one of several
- [x] The choice is logged: every stream's first command, its verdict and the refusal

### 3. Runs

- [ ] How many turns the pick saves, how often a check rejects, whether the bot gets faster
- [ ] Watch for a check that rejects what would have worked. A false rejection is the expensive
      mistake; a miss is caught by the runtime error path, which already works

### 4. Depth, and where it stops

- [ ] After `approach`, check whether the next command would pass — `get` and `put` first. This
      needs `approach`'s check to hand over the cell it would fly to, and `get`/`put` to test the
      interaction point against that cell instead of the engineer's current one
- [ ] Stop there. Validating deeper starts handing the model answers it should be working out
      itself, and that is the line this project does not cross
