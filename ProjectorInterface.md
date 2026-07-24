# Space Engineers — Projector Block Interface (Mod API)

Source: `Sandbox.Common/ModAPI/IMyProjector.cs`, `Ingame/IMyProjector.cs`, `MyProjectorBase.cs`, `MySpaceProjector.cs`.
Both interfaces are whitelisted for mods (`Sandbox.ModAPI` and `Sandbox.ModAPI.Ingame` namespaces allowed entirely).

---

## Interface Hierarchy

```
Sandbox.ModAPI.IMyProjector
  └─ Sandbox.ModAPI.Ingame.IMyProjector
       └─ VRage.Game.ModAPI.IMyFunctionalBlock
            └─ IMyTerminalBlock, IMyEntity, ...
```

## ModAPI Members (Mod-only, NOT available in ingame scripts)

| Member | Type | Description |
|--------|------|-------------|
| `ProjectedGrid` | `IMyCubeGrid {get;}` | The grid currently being projected. `null` if no active projection. |
| `CanBuild(block, checkHavok)` | `BuildCheckResult` | Check if a specific projected block can be built. Returns: `OK`, `NotConnected`, `IntersectedWithGrid`, `IntersectedWithSomethingElse`, `AlreadyBuilt`, `NotFound`. |
| `Build(block, owner, builder, requestInstant)` | `void` | Build a projected block — adds the first component and creates the block. Does NOT remove materials from inventory on its own. |

## Ingame Members (available in mods AND ingame scripts)

### State / Stats (read-only)

| Property | Type | Description |
|----------|------|-------------|
| `IsProjecting` | `bool {get;}` | Whether there is an active projection. |
| `TotalBlocks` | `int {get;}` | Total number of blocks in the projection. |
| `RemainingBlocks` | `int {get;}` | Number of blocks left to be welded (not yet built). |
| `RemainingBlocksPerType` | `Dictionary<MyDefinitionBase, int> {get;}` | Blocks remaining, grouped by type. |
| `RemainingArmorBlocks` | `int {get;}` | Armor blocks left to be welded. |
| `BuildableBlocksCount` | `int {get;}` | Count of blocks that can be built right now (connected + no intersection). |

### Offset / Rotation (read-write)

| Property / Method | Type | Description |
|-------------------|------|-------------|
| `ProjectionOffset` | `Vector3I {get; set;}` | Grid-cell offset of the projection relative to the projector. |
| `ProjectionRotation` | `Vector3I {get; set;}` | Rotation. Values: `0`=no rotation, `1`=90°, `2`=180°. Per axis (X/Y/Z). |
| `UpdateOffsetAndRotation()` | `void` | Call after setting offset/rotation to apply changes. |

> **Obsolete** — do not use: `ProjectionOffsetX/Y/Z`, `ProjectionRotX/Y/Z`.

### Blueprint Loading

| Method | Return | Description |
|--------|--------|-------------|
| `LoadBlueprint(name)` | `bool` | Load a blueprint file and start projecting. `name` is the path to the blueprint directory/file. Returns true on success. |
| `LoadRandomBlueprint(searchPattern)` | `bool` | Load a random blueprint matching `searchPattern` (e.g. `"*.sbc"`). Searches in `Content/Data/Blueprints`. |

---

## Terminal Properties & Actions (accessed by name, NOT via interface fields)

The projector exposes these terminal controls. A mod can read/write them via `GetValue<T>()` / `SetValue()` or invoke actions via `GetAction(id).Apply()`:

| ID | Type | Description |
|----|------|-------------|
| `"ShowOnlyBuildable"` | `checkbox (bool)` | Show only buildable blocks; hide non-buildable ones. Default: `false`. |
| `"KeepProjection"` | `checkbox (bool)` | Keep projection visible after all blocks are built. Default: `false`. |
| `"X"`, `"Y"`, `"Z"` | `slider (float, -50..50)` | Projection offset per axis. Duplicates `ProjectionOffset`. |
| `"RotX"`, `"RotY"`, `"RotZ"` | `slider (float, -2..2)` | Projection rotation per axis. Duplicates `ProjectionRotation`. |
| `"Blueprint"` | `button` | Open blueprint selection screen. Enabled only when projector is working. Supports single-block only. |
| `"Remove"` | `button` | Remove current projection. Enabled only when projecting. |

**ScenarioEditMode-only** (visible/enabled only in creative/scenario mode):

| ID | Type | Description |
|----|------|-------------|
| `"InstantBuilding"` | `checkbox (bool)` | Enable instant building mode. |
| `"GetOwnership"` | `checkbox (bool)` | Set ownership of spawned projections to the projector's owner. |
| `"NumberOfProjections"` | `slider (float, 1..1000)` | Max number of projections that can be spawned. |
| `"NumberOfBlocks"` | `slider (float, 1..10000)` | Max blocks per projection. |
| `"SpawnProjection"` | `button` | Instantly spawn the projected grid as a real grid. |

---

## Answers to Specific Questions

### Can a mod enable/start a specific projection?

**No direct "enable projection" method.** The only programmatic way is:

```csharp
projector.LoadBlueprint("path/to/blueprint");   // loads and starts projecting
```

This works if the projector block is functional (`IsWorking == true`, i.e. powered).

To **disable/remove** a projection, there is no interface method. Options:
- Set `projector.Enabled = false` (from `IMyFunctionalBlock`) — on stop, the projector auto-removes its projection.
- Invoke terminal action: `projector.GetAction("Remove").Apply()`.

### Can a mod control display modes?

**Only one display mode exists:** `ShowOnlyBuildable`.

Not exposed as an interface field — accessible via Terminal API only:

```csharp
bool onlyBuildable = projector.GetValue<bool>("ShowOnlyBuildable");
projector.SetValue("ShowOnlyBuildable", true);
projector.UpdateOffsetAndRotation();  // triggers projection refresh internally
```

Setting `ShowOnlyBuildable` via the terminal setter also calls `OnOffsetsChanged()` internally, which updates the projection visibility immediately.

### Can a mod know which projected blocks are visible?

**No direct list of visible blocks in the interface.** Internal lists (`m_visibleBlocks`, `m_buildableBlocks`, `m_hiddenBlocks`) are private/protected and not exposed.

However, a mod can reconstruct this information because `ProjectedGrid` is accessible:

```csharp
var proj = (IMyProjector)projectorBlock;
if (proj.ProjectedGrid != null)
{
    foreach (var block in proj.ProjectedGrid.CubeBlocks)
    {
        // World position of the projected block
        Vector3 worldPos = proj.ProjectedGrid.GridIntegerToWorld(block.Position);
        // Corresponding real grid position on the projector's parent grid
        Vector3I realPos = ((IMyCubeBlock)projectorBlock).CubeGrid.WorldToGridInteger(worldPos);
        IMySlimBlock realBlock = ((IMyCubeBlock)projectorBlock).CubeGrid.GetCubeBlock(realPos);

        if (realBlock != null && realBlock.BlockDefinition.Id == block.BlockDefinition.Id)
        {
            // Block is already built → hidden
        }
        else if (proj.CanBuild(block, false) == BuildCheckResult.OK)
        {
            // Block is buildable → visible (highlighted as buildable)
        }
        else
        {
            // Block is non-buildable → visible UNLESS ShowOnlyBuildable is true
        }
    }
}
```

Ready-made aggregates from the interface: `BuildableBlocksCount`, `RemainingBlocks`, `TotalBlocks`, `RemainingBlocksPerType` — these give counts, not per-block lists.

---

## Summary Table

| Task | Possible? | How |
|------|-----------|-----|
| Start a projection | ✅ | `LoadBlueprint(path)` or `LoadRandomBlueprint(pattern)` |
| Stop/remove a projection | ⚠️ Indirectly | `Enabled = false` or terminal action `"Remove"` |
| Check if projecting | ✅ | `IsProjecting` |
| Get projected grid | ✅ | `ProjectedGrid` |
| Check/build individual blocks | ✅ | `CanBuild()`, `Build()` |
| Set offset/rotation | ✅ | `ProjectionOffset`, `ProjectionRotation`, `UpdateOffsetAndRotation()` |
| Show only buildable | ✅ via Terminal API | `SetValue("ShowOnlyBuildable", bool)` |
| Keep projection after complete | ✅ via Terminal API | `SetValue("KeepProjection", bool)` |
| Get list of visible blocks | ❌ Not directly | Reconstruct from `ProjectedGrid.CubeBlocks` + `CanBuild()` + real grid check |
| Get remaining blocks counts | ✅ | `RemainingBlocks`, `RemainingBlocksPerType`, etc. |
