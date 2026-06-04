using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace LLE
{
	class TraversabilityCalculator
	{
		private readonly IMyCubeGrid _grid;
		private readonly int _border;

		private List<MyVoxelBase> intersectingVoxels = new List<MyVoxelBase>();

		public TraversabilityCalculator(IMyCubeGrid grid, int border)
		{
			_grid = grid;
			_border = border;

			var worldAabb = _grid.PositionComp.WorldAABB;
			intersectingVoxels.Clear();
			MyGamePruningStructure.GetAllVoxelMapsInBox(ref worldAabb, intersectingVoxels);
		}

		public Traversability Get(Vector3I astarPosition)
		{
			var position = astarPosition + _grid.Min - _border;

			foreach(var voxel in intersectingVoxels)
				if(!IsVoxelTraversable(voxel, position)) return Traversability.Blocked;

			var slim = _grid.GetCubeBlock(position);
			if (slim == null)
				return Traversability.Free;

			Traversability t;
			if (!Collisions._traversabilityCache.TryGetValue(slim.BlockDefinition.Id, out t))
				return Traversability.Blocked;

			if (slim.Min == slim.Max)
			{
				MatrixI m = new MatrixI(slim.Orientation);
				return Traversability.Rotate(t, m);
			}
			return Traversability.Blocked;
		}

		private bool IsVoxelTraversable(MyVoxelBase voxel, Vector3I gridPosition)
		{
			var v1 = _grid.GridIntegerToWorld(gridPosition);
			var v2 = _grid.GridIntegerToWorld(gridPosition+1);

			var v = v2 - v1;
			v1 -= v * 0.5;
			v2 -= v * 0.5;

			BoundingBoxD wb = new BoundingBoxD(v1, v2);

			bool i1 = HasMaterialsInBox(wb, intersectingVoxels[0]); // 0.02 // Super fast
			//bool i2 = voxel.IsAnyAabbCornerInside(ref MatrixD.Identity, wb); // 0.04 // A bit slower
			//BoundingSphereD sphere = new BoundingSphereD(vc, 1.25); // 0.2 // Very slow
			//bool i3 = voxel.GetIntersectionWithSphere(ref sphere);

			return !i1;
		}

		private static readonly MyStorageData storage = new MyStorageData();

		public static bool HasMaterialsInBox(BoundingBoxD worldBoundaries, MyVoxelBase voxel, int lod = 0)
		{
			if (voxel == null || voxel.MarkedForClose) return false;

			Vector3I max = voxel.Storage.Size - 1;
			Vector3D bottomLeftCorner = voxel.PositionLeftBottomCorner;
			Vector3I voxelCoordMin, voxelCoordMax;

			MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref worldBoundaries.Min, out voxelCoordMin);
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref worldBoundaries.Max, out voxelCoordMax);
			Vector3I voxelCoord3 = voxelCoordMin - 1;
			Vector3I voxelCoord4 = voxelCoordMax + 1;

			Vector3I.Clamp(ref voxelCoord3, ref Vector3I.Zero, ref max, out voxelCoord3);
			Vector3I.Clamp(ref voxelCoord4, ref Vector3I.Zero, ref max, out voxelCoord4);

			voxelCoord3 >>= lod;
			voxelCoord3 -= 1;
			voxelCoord4 >>= lod;
			voxelCoord4 += 1;

			storage.Resize(voxelCoord3, voxelCoord4);

			if (voxel == null || voxel.MarkedForClose) return false;

			using (voxel.Pin())
			{
				voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.Material, lod, voxelCoord3, voxelCoord4);
			}

			Vector3I vector3I = default(Vector3I);
			vector3I.X = voxelCoord3.X;
			while (vector3I.X <= voxelCoord4.X)
			{
				vector3I.Y = voxelCoord3.Y;
				while (vector3I.Y <= voxelCoord4.Y)
				{
					vector3I.Z = voxelCoord3.Z;
					while (vector3I.Z <= voxelCoord4.Z)
					{
						Vector3I p = vector3I - voxelCoord3;
						int linearIdx = storage.ComputeLinear(ref p);
						byte b = storage.Material(linearIdx);

						if (b != byte.MaxValue)
						{
							return true;
						}

						vector3I.Z++;
					}
					vector3I.Y++;
				}
				vector3I.X++;
			}

			return false;
		}
	}
}
