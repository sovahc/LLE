# Space Engineers: Use Object Detectors

## Overview

Detectors are **model dummies (empty transforms)**, not separate models and not Havok collision shapes from the model file.

## How It Works

### 1. Definition in Model (.mwm)

Block models contain dummies named `detector_<name>`. Examples:
- `detector_door` — door interaction zone
- `detector_terminal` — terminal interaction zone
- `detector_ladder` — ladder zone

A dummy is simply a matrix (position + rotation + scale) embedded in the model.

### 2. Loading (`MyUseObjectsComponent.LoadDetectorsFromModel`)

Iterates over all model dummies, filters by `detector_` prefix, splits the name, and registers each as a detector:

```
dummy name "detector_door" → detector name "door"
```

### 3. Collision Generated at Runtime (`RecreatePhysics`)

For each detector, a **unit cube** (`-0.5..0.5`) is transformed by the dummy's matrix, then wrapped in `HkConvexVerticesShape`. All detector shapes are combined into a single `HkListShape` with `RBF_DISABLE_COLLISION_RESPONSE`.

```
Unit cube × dummy matrix → HkConvexVerticesShape
```

## Summary

| Aspect | Source |
|--------|--------|
| Detector position/size | Dummy matrix in .mwm model |
| Interaction type | Dummy name (`door`, `terminal`, `ladder`) → `MyUseObjectFactory` |
| Detector collision | Generated at runtime: unit cube × dummy matrix → `HkConvexVerticesShape` |
| Havok body | `HkListShape` of all detectors, `RBF_DISABLE_COLLISION_RESPONSE` |

## Key Points

- Detectors are **always parallelepipeds** (transformed unit cubes).
- They are **never** derived from the model's Havok collision data.
- They exist **independently** from the block's main collision shape.
- Custom detectors can be added programmatically via `AddDetector(name, matrix)`.

## Interactive Detector Types

Registered via `[MyUseObject("name")]` attribute in `MyUseObjectFactory`. Only these create collision shapes and allow interaction.

| Detector Name | Class | Supported Actions | Description |
|--------------|-------|-------------------|-------------|
| `terminal` | `MyUseObjectTerminal` | `OpenTerminal`, `OpenInventory` | Opens block GUI / inventory. **Most universal** — works on any `MyCubeBlock`. |
| `door` | `MyUseObjectAirtightDoors` | `Manipulate`, `OpenTerminal` | Open/close airtight door, open terminal. |
| `advanceddoor` | `MyUseObjectAdvancedDoorTerminal` | `Manipulate`, `OpenTerminal` | Same for Advanced Door. |
| `inventory` | `MyUseObjectInventory` | `OpenInventory`, `OpenTerminal` | Opens inventory (works on any `MyEntity`). |
| `conveyor` | `MyUseObjectInventory` | `OpenInventory`, `OpenTerminal` | Alias — same class as `inventory`. |
| `panel` | `MyUseObjectPanelButton` | `Manipulate`, `OpenTerminal` | Press button panel slot, open button config. |
| `cockpit` | `MyUseObjectCockpitDoor` | `Manipulate` | Enter cockpit (take control). |
| `block` | `MyUseObjectMedicalRoom` | `Manipulate`, `OpenTerminal` | Heal in medical room (continuous usage). |
| `wardrobe` | `MyUseObjectWardrobe` | `Manipulate` | Open wardrobe (suit change). |
| `textpanel` | `MyUseObjectTextPanel` | `Manipulate`, `OpenTerminal` | Show screen, open edit terminal. |
| `cryopod` | `MyUseObjectCryoChamberDoor` | `Manipulate` | Enter cryochamber. |

### Special Case

- **`terminal` on a Door** → `MyUseObjectDoorTerminal` (hardcoded hack in `CreateInteractiveObject`): behaves like `door`.

## Non-Interactive Detectors

These names are **not** registered in `MyUseObjectFactory` — `CreateInteractiveObject` returns `null`, no collision shape is created. They are handled separately by other game systems:

- **`ownership`** — block ownership zone (handled outside UseObject system)
- **`ladder`** — ladder climbing zones
- **`shiptool`** — ship tool construction/dismantling zones
- **`maintenance`** — maintenance mode interaction zones

## UseActionEnum Flags

| Flag | Value | Meaning |
|------|-------|---------|
| `None` | 0 | No action |
| `Manipulate` | 1 << 0 | Primary use (USE key) |
| `OpenTerminal` | 1 << 1 | Open terminal GUI (TERMINAL key) |
| `OpenInventory` | 1 << 2 | Open inventory GUI |
| `UseFinished` | 1 << 3 | USE key released |
| `Close` | 1 << 4 | Use object closing (character lost sight) |
| `PickUp` | 1 << 5 | Pick up object |
