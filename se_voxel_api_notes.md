# Space Engineers Voxel API Research & Workarounds

## 1. Original `HasMaterialsInBox` (Game Source)
**Location:** `Sandbox.Game/Game/Entities/MyVoxelMap.cs`

The original implementation is a static helper that wraps the storage reading logic. It expands the requested bounding box to ensure full coverage.

```csharp
public static bool HasMaterialsInBox(MyVoxelMap voxel, BoundingBoxD box, int lod = 0)
{
    Vector3I max = voxel.Storage.Size - new Vector3I(1);
    Vector3D bottomLeftCorner = voxel.PositionLeftBottomCorner;
    Vector3I voxelCoordMin, voxelCoordMax;

    // Convert world coords to local voxel coords
    MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref box.Min, out voxelCoordMin);
    MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref box.Max, out voxelCoordMax);
    
    // Expand range by 1 voxel in all directions for safety
    Vector3I vector3I = voxelCoordMin - new Vector3I(1);
    Vector3I vector3I_0 = voxelCoordMax + new Vector3I(1);

    // Clamp to storage bounds
    Vector3I.Clamp(ref vector3I, MyVector3I.Zero, max, out vector3I);
    Vector3I.Clamp(ref vector3I_0, MyVector3I.Zero, max, out vector3I_0);

    // Adjust for LOD (shift right) and expand again
    vector3I = new Vector3I(vector3I.X >> lod, vector3I.Y >> lod, vector3I.Z >> lod);
    vector3I -= new Vector3I(1);
    vector3I_0 = new Vector3I(vector3I_0.X >> lod, vector3I_0.Y >> lod, vector3I_0.Z >> lod);
    vector3I_0 += new Vector3I(1);

    // Read data into a flat buffer
    using (MyStorageData storage = new MyStorageData())
    {
        storage.Resize(vector3I, vector3I_0);
        if (!voxel.MarkedForClose)
            voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.Material, lod, vector3I, vector3I_0);

        // Iterate over the buffer looking for non-empty material
        Vector3I vector3I_1 = default(Vector3I);
        vector3I_1.X = vector3I.X;
        while (vector3I_1.X <= vector3I_0.X)
        {
            vector3I_1.Y = vector3I.Y;
            while (vector3I_1.Y <= vector3I_0.Y)
            {
                vector3I_1.Z = vector3I.Z;
                while (vector3I_1.Z <= vector3I_0.Z)
                {
                    Vector3I p = vector3I_1 - vector3I;
                    int linearIdx = storage.ComputeLinear(ref p);
                    byte b = storage.Material(linearIdx);

                    if (b != byte.MaxValue) // 255 is empty
                        return true;

                    vector3I_1.Z++;
                }
                vector3I_1.Y++;
            }
            vector3I_1.X++;
        }
    }

    return false;
}
```

---

## 2. `ReadRange` Analysis
**Location:** `MySparseOctree.cs`

The method `internal unsafe void ReadRange` is the core engine function for extracting voxel data.

### How it works:
1.  **Iterative DFS:** Uses a stack (`stackalloc`) to traverse the Octree from root (coarsest LOD) down to the target LOD.
2.  **Intersection Logic:** It calculates if child nodes overlap with the requested bounding box. If not, they are skipped.
3.  **Expansion (Upscaling):** This is the critical performance bottleneck for large queries.
    *   If the tree has no data at a finer level (e.g., you ask for LOD 0 but the tree only goes down to LOD 2), it treats the coarse node as a uniform block.
    *   It then **iterates through every voxel** in that block and writes the same value into the output buffer (`target.Set(...)`).

### Why this is bad for large queries:
If you request a massive area (e.g., 1000x1000x1000) and the interior is uniform empty space, `ReadRange` will still allocate memory and write `byte.MaxValue` to every single voxel in that volume because it "flattens" the hierarchy into a dense array.

---

## 3. The "Hack": Direct Octree Access via Reflection
Since there is no official API for hierarchical traversal without expansion, you must access internal structures using Reflection.

### Internal Structure: `MyOctreeStorage`
The actual storage implementation contains four private dictionaries that hold the tree nodes. These allow random access to any node at any LOD without traversing or expanding.

```csharp
// Private fields inside MyOctreeStorage
private readonly Dictionary<UInt64, MyOctreeNode> m_contentNodes;
private readonly Dictionary<UInt64, IMyOctreeLeafNode> m_contentLeaves;
private readonly Dictionary<UInt64, MyOctreeNode> m_materialNodes;
private readonly Dictionary<UInt64, IMyOctreeLeafNode> m_materialLeaves;
```

### Access Pattern:
1.  **Get Storage:** `voxel.Storage` (cast to `MyOctreeStorage`).
2.  **Reflection:** Get the `m_materialNodes` dictionary field.
3.  **Key Generation:** Use `MyCellCoord.PackId64Static(lod, coord)` to generate the dictionary key.

### Code Example:
```csharp
// Cache this FieldInfo at mod init
var materialNodesField = typeof(MyOctreeStorage).GetField("m_materialNodes", BindingFlags.NonPublic | BindingFlags.Instance);

// Usage
var storage = voxel.Storage as MyOctreeStorage;
if (storage != null)
{
    var dict = (Dictionary<UInt64, MyOctreeNode>)materialNodesField.GetValue(storage);
    
    // Check a node at LOD 2, coordinate (10, 5, 10)
    ulong key = MyCellCoord.PackId64Static(2, new Vector3I(10, 5, 10));
    MyOctreeNode node;
    
    if (dict.TryGetValue(key, out node))
    {
        // node.Data[0..7] contains the material values for children
        // node.ChildMask indicates existence of deeper nodes
        // byte.MaxValue == 255 means Empty
    }
}
```

### Efficient Traversal Strategy:
Instead of `ReadRange`, implement a custom recursive walker:
1.  Start at Root (`lod = TreeHeight - 1`).
2.  Check AABB intersection with the current node's volume.
3.  **Fast Reject:** If fully inside, check `node.Data`. If all empty (255), return false immediately.
4.  **Descent:** If partial overlap or mixed data, calculate child keys (`lod - 1`, coord * 2) and recurse.

---

## 4. Caveats & Warnings
*   **Thread Safety:** The dictionaries are not thread-safe for writing. Always use `using (voxel.Pin())` when reading to prevent storage unloading or reorganization during your access.
*   **Versioning:** Field names (`m_materialNodes`) might change in game updates. Cache `FieldInfo` on load and handle exceptions if they become null.
*   **Planets:** This structure applies to Asteroids/Standard Voxel Maps (`MyVoxelMap`). Planets use `MyPlanetStorageProvider` which has a different chunk-based architecture.
*   **Safety:** This is unsupported internal API usage. It bypasses standard checks and could crash if the game state changes unexpectedly (though rare for read-only access).

---

## 5. Summary of Options

| Method | Pros | Cons | Use Case |
| :--- | :--- | :--- | :--- |
| **Official `ReadRange`** | Safe, supported, fast for small areas. | Allocates memory, expands data (slow for large uniform areas). | Small local checks, voxel carving. |
| **Reflection Hack** | O(1) access to nodes, no expansion, extremely fast for empty space. | Fragile (updates), requires unsafe/reflection, harder to implement. | Large-scale navigation maps, pathfinding, chunk loading. |
