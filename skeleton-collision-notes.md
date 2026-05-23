# Skeleton-based Collision for Armor Blocks

## Overview

Armor blocks (BlockTopology == Cube) don't have collision shapes in their .mwm models. Instead, the game builds collision geometry from the block's skeleton bones at runtime.

## Key Sources

- `MyCubeBlockCollector.cs` — main collection logic, `CollectBlock()` and `AddConvexShape()`
- `MyGridSkeleton.cs` — skeleton data structure, bone serialization/deserialization
- `MyBlockVerticesCache.cs` — precomputed vertex positions per topology
- `MyGridShape.cs` — grid-level shape assembly
- `Vector3UByte.cs` — bone offset normalization/denormalization

## Flow: How Armor Block Collisions Are Built

### 1. CollectBlock() — Decision Point

**Source:** `MyCubeBlockCollector.cs:305-432`

```
BlockTopology == Cube
  └─> PhysicsOption.Box      → AddBoxes() — simple AABB boxes per grid cell
  └─> PhysicsOption.Convex   → AddConvexShape() — convex hull from skeleton bones
```

The decision between Box and Convex (line 319-327):
- If `ENABLE_SIMPLE_GRID_PHYSICS` → always Box
- If `CubeTopology == Box` AND skeleton is deformed → Convex
- Otherwise → Box

### 2. AddConvexShape() — The Core Algorithm

**Source:** `MyCubeBlockCollector.cs:455-480`

```csharp
void AddConvexShape(MySlimBlock block, bool applySkeleton)
{
    var blockPos = block.Min * block.CubeGrid.GridSize;
    var bonePos = block.Min * MyGridSkeleton.BoneDensity + 1;  // BoneDensity = 2
    var skeleton = block.CubeGrid.Skeleton;

    foreach (var point in MyBlockVerticesCache.GetBlockVertices(topology, orientation))
    {
        var pointBonePos = bonePos + Vector3I.Round(point);
        var vert = point * block.CubeGrid.GridSizeHalf;
        if (skeleton.TryGetBone(ref pointBonePos, out pointBone))
            vert.Add(pointBone);
        m_tmpHelperVerts.Add(vert + blockPos);
    }

    Shapes.Add(new HkConvexVerticesShape(verts, count, shrink, radius));
}
```

**Key constants:**
- `BoneDensity = 2` — each grid cell has a 3×3×3 grid of bones (positions 0..2)
- `ADD_INNER_BONES_TO_CONVEX = true` — includes inner bone positions for more accurate hull
- `SHRINK_CONVEX_SHAPE = false` — no shrinking applied
- `PhysicsConvexRadius = 0.05f` — contact margin

### 3. Bone Coordinate System

**Source:** `MyGridSkeleton.cs:36` — `BoneDensity = 2`

For a block at grid position `(bx, by, bz)`:
- Bone positions range from `(bx*2, by*2, bz*2)` to `(bx*2+2, by*2+2, bz*2+2)`
- That's 27 bones per block (3×3×3)

**Bone base offset:** `bonePos = block.Min * BoneDensity + 1` (line 464)
- The `+1` centers the bone coordinates around the block center

### 4. MyBlockVerticesCache — Vertex Positions per Topology

**Source:** `MyBlockVerticesCache.cs:62-448`

Vertices are defined in **bone space** (range -1..1), relative to block center. Each topology has its own set of vertices.

**Box topology** (line 236-271):
- 8 corner vertices: (±1, ±1, ±1)
- 19 inner bone positions (when `ADD_INNER_BONES_TO_CONVEX = true`)

**Slope topology** (line 66-91):
- 6 main corners
- 9 inner bones

**Corner topology** (line 121-141):
- 4 main corners
- 6 inner bones

Vertices are precomputed for all 36 valid orientations (6 Forward × 6 Up, excluding opposites).

### 5. Bone Offset Storage and Denormalization

**Source:** `Vector3UByte.cs:127-145`

Bone offsets are stored as `Vector3UByte` (3 bytes, 0-255 each):

```csharp
// Normalize: Vector3 → Vector3UByte
// Scale from (-range, range) to (0, 255)
var v = (vec / range / 2 + new Vector3(0.5f)) * 255f;

// Denormalize: Vector3UByte → Vector3
float epsilon = 0.5f / 255.0f;
return (vec / 255.0f - new Vector3(0.5f - epsilon)) * 2 * range;
```

**boneRange = GridSize** (source: `MyCubeGrid.cs:5221`)

**IsMiddle check** (line 117-119): `Vector3UByte(127, 127, 127)` means zero offset — bone is at default position.

### 6. SBC File Structure for Armor Blocks

**Source:** `CubeBlocks_Armor.sbc`

Example: `LargeBlockArmorBlock`
```xml
<Definition>
    <Id>
        <TypeId>CubeBlock</TypeId>
        <SubtypeId>LargeBlockArmorBlock</SubtypeId>
    </Id>
    <CubeSize>Large</CubeSize>
    <BlockTopology>Cube</BlockTopology>
    <Size x="1" y="1" z="1" />
    <CubeDefinition>
        <CubeTopology>Box</CubeTopology>
        <Sides>
            <Side Model="Models\Cubes\Large\Armor\SquarePlate.mwm" ... />
            ... (6 sides, all same model)
        </Sides>
    </CubeDefinition>
    <Skeleton>
        <BoneInfo>
            <BonePosition x="0" y="0" z="0" />
            <BoneOffset x="127" y="127" z="127" />
        </BoneInfo>
        ... (27 bones for 3×3×3 grid)
    </Skeleton>
</Definition>
```

- `BonePosition` is in bone coordinates (0..2 for a single block)
- `BoneOffset` is `Vector3UByte` normalized with `boneRange = GridSize`
- Value 127 = no deformation (default position)

## Algorithm Summary for Extractor

To build a convex hull for an armor block from SBC data:

1. Parse `<Skeleton><BoneInfo>` entries → map `BonePosition` → `BoneOffset`
2. Denormalize offsets: `Vector3UByte.Denormalize(offset, gridSize)`
3. Get topology from `<CubeDefinition><CubeTopology>` (default: Box)
4. Get vertex list for that topology from `MyBlockVerticesCache` logic
5. For each vertex `v` (in bone space -1..1):
   - Bone position: `bonePos = blockMin * 2 + 1 + Round(v)`
   - World vertex: `v * gridSizeHalf + boneOffset[bonePos] + blockMin * gridSize`
6. Build `ConvexHullShape` from all world vertices

**Note:** For undeformed blocks (all offsets = 127), the result is a simple box. The convex hull is only needed when bones are deformed.
