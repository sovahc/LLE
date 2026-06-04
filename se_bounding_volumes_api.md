# Space Engineers — Bounding Volumes & Intersection API

All three types are **public structs** in `VRageMath`

---

## 1. `BoundingSphereD` (Bounding Sphere)

```csharp
var sphere = grid.PositionComp.WorldVolume;   // BoundingSphereD in world space
```

| Method | Description | Complexity |
|--------|-------------|------------|
| `Intersects(BoundingSphereD)` | Sphere ↔ sphere: `dist² < (r1 + r2)²` | ~10 ops |
| `Intersects(BoundingBoxD)` | Sphere ↔ AABB | — |
| `Contains(BoundingSphereD)` | Returns `ContainmentType` | — |
| `Contains(Vector3D)` | Point in sphere → `ContainmentType` | — |

---

## 2. `BoundingBoxD` (Axis-Aligned Bounding Box)

```csharp
var localAABB  = grid.PositionComp.LocalAABB;   // BoundingBoxD in local grid space
var worldAABB  = grid.PositionComp.WorldAABB;   // BoundingBoxD in world space
```

| Method | Description | Complexity |
|--------|-------------|------------|
| `Intersects(BoundingBoxD)` | AABB ↔ AABB: 6 comparisons (Min/Max per axis) | ~6 ops |
| `Intersects(BoundingSphereD)` | AABB ↔ sphere via clamp + distance | — |

---

## 3. `MyOrientedBoundingBoxD` (Oriented Bounding Box)

**Not exposed directly on `PositionComp`.** Mods construct it manually from grid data:

```csharp
var hExtents = grid.PositionComp.LocalAABB.HalfExtents;
var center   = grid.PositionComp.WorldAABB.Center;
var quat     = Quaternion.CreateFromRotationMatrix(grid.WorldMatrix);
var obb      = new MyOrientedBoundingBoxD(center, hExtents, quat);
```

| Method | Description | Complexity |
|--------|-------------|------------|
| `Intersects(ref MyOrientedBoundingBoxD)` | OBB ↔ OBB: full SAT, 15 separating axes | ~200+ ops |
| `Intersects(ref BoundingBoxD)` | OBB ↔ AABB: simplified SAT | — |
| `Intersects(ref BoundingSphereD)` | OBB ↔ sphere: transform to local space + clamp | ~30 ops |
| `Contains(ref Vector3D)` | Point inside OBB | — |
| `Contains(ref MyOrientedBoundingBoxD)` | Returns `ContainmentType` (Disjoint / Intersects / Contains) | — |
| `GetAABB()` | Tightest AABB wrapping the OBB | — |
| `Intersects(ref RayD)` | Returns parametric distance `double?` or `null` | — |
| `Intersects(ref LineD)` | Same, clamped to segment length | — |
| `GetCorners(Vector3D[], int)` | Writes 8 world-space corners into array | — |

---

## Two-Stage Intersection Check

### Step 1 — Fast (sphere ↔ sphere)

```csharp
if (!gridA.PositionComp.WorldVolume.Intersects(gridB.PositionComp.WorldVolume))
    return false; // no intersection, exit early
```

### Step 2 — Precise (OBB ↔ OBB via SAT)

```csharp
var obbA = new MyOrientedBoundingBoxD(centerA, extentsA, quatA);
var obbB = new MyOrientedBoundingBoxD(centerB, extentsB, quatB);
bool intersects = obbA.Intersects(ref obbB); // 15 separating axes
```

### Intermediate — OBB ↔ sphere (faster than full SAT)

```csharp
var sphereB = gridB.PositionComp.WorldVolume;
bool intersects = obbA.Intersects(ref sphereB);
```

---

## Nested Bounding Boxes

AABB/OBB do **not** guarantee nesting. An asteroid may fully contain a ship by volume, but `obbShip.Intersects(ref obbAsteroid)` still returns `true` (they overlap).

SAT correctly handles full containment — `Contains()` returns `ContainmentType.Contains`, while `Intersects()` simply returns `true`.

To distinguish "overlapping" from "one inside another":

```csharp
var containment = obbAsteroid.Contains(ref obbShip);
// Disjoint / Intersects / Contains
```

---

## Quick Reference

| Type | Available to mods | Source |
|------|:---:|---|
| `BoundingSphereD` | ✅ | `entity.PositionComp.WorldVolume` |
| `BoundingBoxD` (AABB) | ✅ | `entity.PositionComp.WorldAABB` / `LocalAABB` |
| `MyOrientedBoundingBoxD` (OBB) | ✅ | Construct from `LocalAABB.HalfExtents` + `WorldMatrix` |

All types are `struct`. All intersection methods are public and work without reflection.

---

## Source References

- `~/Projects/SpaceEngineers_Source/Sources/VRage.Math/BoundingSphereD.cs`
- `~/Projects/SpaceEngineers_Source/Sources/VRage.Math/BoundingBoxD.cs`
- `~/Projects/SpaceEngineers_Source/Sources/VRage.Math/MyOrientedBoundingBoxD.cs`
