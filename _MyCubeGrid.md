# MyCubeGrid useful elements

+ HashSet<MyCubeBlock> Inventories;

* UnsafeBlocks
  `MyCubeGrid.UnsafeBlocks` — a game mechanism that warns the player about rotors/pistons/connectors with settings capable of causing damage (high torque, high impulse, disabled shared inertia tensor).

* OccupiedBlocks - MyCockpit with a non-empty Pilot
* NaturalGravity
* IsPowerSwitchOn - turn off (or on) all producers on the grid/group at once + store this in a master flag
* IsParked
* NumberOfGridColors => m_colorStatistics.Count;

## public MyCubeGridSystems GridSystems { get; private set; } // many systems here

* DampenersEnabled
* IsUnsupportedStation

* public float GridSize -> <CubeSizes Large="2.5" Small="0.5" />
* public Vector3 LinearVelocity

## Actions

* ! OnMinMaxChanged;
* PowerSwitchChanged;
* SpeedChanged;

* ? GridPresenceTierChanged;
* ? PlayerPresenceTierChanged;
* ? OnNaturalGravityChanged;

* ! OnBlockAddedGlobally;
* ! OnBlockRemovedGlobally;

## Other

* public static bool TryRayCastGrid(ref LineD worldRay, out MyCubeGrid hitGrid, out Vector3D worldHitPos)

