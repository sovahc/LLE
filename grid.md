#useful

    IsSolarOccluded
    
    + HashSet<MyCubeBlock> Inventories;

    public HashSetReader<MyCubeBlock> UnsafeBlocks => m_unsafeBlocks;

    public HashSetReader<MyDecoy> Decoys => m_decoys;

    public HashSetReader<MyCockpit> OccupiedBlocks => m_occupiedBlocks;

    public Vector3 NaturalGravity => m_gravity;

    
    public bool IsPowerSwitchOn => m_isPowerSwitchOn.Value;

    public bool IsParked

    public bool IsSmokeParticleActive

    public int NumberOfGridColors => m_colorStatistics.Count;

    public bool IsSplit { get; set; } // ??

    public MyCubeGridSystems GridSystems { get; private set; } // many systems here

    public bool DampenersEnabled => m_dampenersEnabled;

    public bool MarkedAsTrash => m_markedAsTrash;

    public bool IsUnsupportedStation { get; private set; }

    public float GridSize { get; private set; }

    public float GridScale { get; private set; }

    public float GridSizeHalf { get; private set; }

    public Vector3 GridSizeHalfVector { get; private set; }

    public float GridSizeQuarter { get; private set; }

    public Vector3 GridSizeQuarterVector { get; private set; }

    public Vector3 LinearVelocity


    public event Action<MyCubeGrid, Vector3I, Vector3I> OnMinMaxChanged;

    public event Action<MyCubeGrid> OnSolarOccludedChanged;

    public event Action<bool> PowerSwitchChanged;

    public event Action<VRage.Game.ModAPI.IMyCubeGrid> SpeedChanged;

    public event Action<MyCubeGrid> GridPresenceTierChanged;

    public event Action<MyCubeGrid> PlayerPresenceTierChanged;

    public event Action<VRage.Game.ModAPI.IMyCubeGrid> OnNaturalGravityChanged;

    //
    // Summary:
    //     Called only for single block changes
    public static event Action<MySlimBlock> OnBlockAddedGlobally;

    //
    // Summary:
    //     Not called on grid split, or when closing grid
    public static event Action<MySlimBlock> OnBlockRemovedGlobally;

    public static bool TryRayCastGrid(ref LineD worldRay, out MyCubeGrid hitGrid, out Vector3D worldHitPos)

