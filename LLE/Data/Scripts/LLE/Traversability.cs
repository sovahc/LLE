using System;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Voxels;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace LLE
{
	/// <summary>
	/// Stores block traversability data as a 3x3x3 bit cube.
	/// Indexed from -1 to 1 along each axis.
	/// </summary>
	public struct Traversability
	{
		private static readonly uint All_1 = (1u << 27) - 1;

		public static readonly Traversability Blocked = new Traversability(All_1);
		public static readonly Traversability Free = new Traversability(0);

		private uint _mask;

		public Traversability(uint mask)
		{	_mask = mask;
		}

		private void Check(int dx, int dy, int dz)
		{	if (dx < -1 || dx > 1 || dy < -1 || dy > 1 || dz < -1 || dz > 1)
				throw new Exception($"Traversability index out of range: {dx}, {dy}, {dz}");
		}

		private int Index(int x, int y, int z)
		{	Check(x, y, z);	
			return (x + 1) * 9 + (y + 1) * 3 + (z + 1);
		}

		private int Index(Vector3I v)
		{	return Index(v.X, v.Y, v.Z);
		}

		public bool this[int x, int y, int z]
		{
			get
			{	return (_mask & (1u << Index(x, y, z))) != 0;
			}
			set
			{	if (value)
					_mask |= (1u << Index(x, y, z));
				else
					_mask &= ~(1u << Index(x, y, z));
			}
		}

		public bool this[Vector3I v]
		{
			get
			{	return this[v.X, v.Y, v.Z];
			}
			set
			{	this[v.X, v.Y, v.Z] = value;
			}
		}

		/// <summary>
		/// Whether the engineer can turn around in the center of the block.
		/// </summary>
		public bool Center => this[new Vector3I(0,0,0)];

		public void Clear() => _mask = 0;

		public void SetAll(bool value)
		{
			if (value)
				_mask = All_1;
			else
				_mask = 0;
		}

		public static Traversability Rotate(Traversability src, MatrixI rotation)
		{
			Vector3I v, v2;
			Traversability result = new Traversability();
			for (v.Z = -1; v.Z <= 1; ++v.Z)
				for (v.Y = -1; v.Y <= 1; ++v.Y)
					for (v.X = -1; v.X <= 1; ++v.X)
					{
						Vector3I.TransformNormal(ref v, ref rotation, out v2);
						result[v2] = src[v];
					}
			return result;
		}

		public override string ToString()
		{
			var sb = new StringBuilder();
			for (int z = 1; z >= -1; --z)
			{
				for (int y = 1; y >= -1; --y)
				{
					for (int x = -1; x <= 1; ++x)
						sb.Append(this[x, y, z] ? "#" : ".");
					sb.Append(' ');
				}
				sb.Append('|');
			}
			return sb.ToString();
		}
	}

	class TraversabilityCalculator
	{
		private readonly IMyCubeGrid grid;
		private readonly int border;
		private readonly List<MyVoxelBase> intersectingVoxels = new List<MyVoxelBase>();
		private readonly List<IMyCubeGrid> intersectingGrids = new List<IMyCubeGrid>();

		public TraversabilityCalculator(IMyCubeGrid grid_, int border_)
		{
			grid = grid_;
			border = border_;

			var worldAabb = grid.PositionComp.WorldAABB;
			worldAabb.Inflate(border * grid.GridSize);
			intersectingVoxels.Clear();
			MyGamePruningStructure.GetAllVoxelMapsInBox(ref worldAabb, intersectingVoxels);

			intersectingGrids.Clear();
			foreach (var entity in MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref worldAabb))
			{
				var g = entity as IMyCubeGrid;
				if (g != null && g != grid)
					intersectingGrids.Add(g);
			}
		}

		public Traversability GetForAstar(Vector3I astarPosition)
		{
			var position = astarPosition + grid.Min - border;
			return GetTraversability(position);
		}

		public Traversability GetTraversability(Vector3I position)
		{
			// Small grid blocks are always treated as fully blocked.
			if (grid.GridSizeEnum == MyCubeSize.Small)
			{	
				// !not optimized!
				Vector3I v;
				for(v.Z = -1; v.Z <= 1; ++v.Z)
					for(v.Y = -1; v.Y <= 1; ++v.Y)
						for(v.X = -1; v.X <= 1; ++v.X)
						{	if(grid.GetCubeBlock(position + v) != null) return Traversability.Blocked;
						}
				
				return Traversability.Free;
			}

			foreach(var voxel in intersectingVoxels)
				if(!IsVoxelTraversable(voxel, position)) return Traversability.Blocked;

			var center = grid.GridIntegerToWorld(position);
			foreach (var g in intersectingGrids)
				if (Collisions.CheckSphereVsGrid(g, center, Constants.CollisionProbeRadius))
					return Traversability.Blocked;

			var slim = grid.GetCubeBlock(position);
			if (slim == null)
				return Traversability.Free;

			return Collisions.GetBlockTraversability(slim, position);
		}

		private bool IsVoxelTraversable(MyVoxelBase voxel, Vector3I gridPosition)
		{
			var v1 = grid.GridIntegerToWorld(gridPosition);
			var v2 = grid.GridIntegerToWorld(gridPosition+1);
			var vc = v1;

			var v = v2 - v1;
			v1 -= v * 0.5;
			v2 -= v * 0.5;

			BoundingBoxD wb = new BoundingBoxD(v1, v2);

			bool hasMaterials, hasSpace;

			HasMaterialsInBox(wb, voxel, 0, out hasMaterials, out hasSpace); // 0.02 // Super fast
			//bool i2 = voxel.IsAnyAabbCornerInside(ref MatrixD.Identity, wb); // 0.04 // A bit slower
			if(hasMaterials && hasSpace)
			{	BoundingSphereD sphere = new BoundingSphereD(vc, 1.25); // 0.2 // Very slow but precise
				return ! voxel.GetIntersectionWithSphere(ref sphere);
			}

			return hasSpace;
		}

		private static readonly MyStorageData storage = new MyStorageData();

		public static void HasMaterialsInBox(BoundingBoxD worldBoundaries, MyVoxelBase voxel, int lod,
			out bool hasMaterials, out bool hasSpace)
		{
			hasMaterials = false;
			hasSpace = false;

			if (voxel == null || voxel.MarkedForClose) { hasSpace = true; return; }

			Vector3I storageSizeMax = voxel.Storage.Size - 1;
			Vector3D bottomLeftCorner = voxel.PositionLeftBottomCorner;
			Vector3I voxelCoordMin, voxelCoordMax;

			MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref worldBoundaries.Min, out voxelCoordMin);
			MyVoxelCoordSystems.WorldPositionToVoxelCoord(bottomLeftCorner, ref worldBoundaries.Max, out voxelCoordMax);
			Vector3I min = voxelCoordMin - 1;
			Vector3I max = voxelCoordMax + 1;

			Vector3I.Clamp(ref min, ref Vector3I.Zero, ref storageSizeMax, out min);
			Vector3I.Clamp(ref max, ref Vector3I.Zero, ref storageSizeMax, out max);

			min >>= lod;
			min -= 1;
			max >>= lod;
			max += 1;

			storage.Resize(min, max);

			if (voxel == null || voxel.MarkedForClose) { hasSpace = true; return; }

			using (voxel.Pin())
			{
				voxel.Storage.ReadRange(storage, MyStorageDataTypeFlags.Material, lod, min, max);
			}

			Vector3I v = default(Vector3I);
			v.X = min.X;
			while (v.X <= max.X)
			{
				v.Y = min.Y;
				while (v.Y <= max.Y)
				{
					v.Z = min.Z;
					while (v.Z <= max.Z)
					{
						Vector3I p = v - min;
						int linearIndex = storage.ComputeLinear(ref p);
						byte b = storage.Material(linearIndex);

						if (b > 127) hasSpace = true;
						else hasMaterials = true;

						v.Z++;
					}
					v.Y++;
				}
				v.X++;
			}
		}
	}
}
