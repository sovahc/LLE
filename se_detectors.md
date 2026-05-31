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
