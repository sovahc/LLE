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
	// Indexed from -1 to 1 along each axis.
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

		// Whether the engineer can turn around in the center of the block.
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
		private readonly int resolutionBits;

		public Vector3I? engineerCell;

		public TraversabilityCalculator(IMyCubeGrid grid_, int resolutionBits_)
		{
			grid = grid_;
			resolutionBits = resolutionBits_;
		}

		public BoundingBoxD CellRangeToWorld(Vector3I cellMin, Vector3I cellMax)
		{
			var min = cellMin >> resolutionBits;
			var max = cellMax >> resolutionBits;

			var box = BoundingBoxD.CreateInvalid();

			for (int i = 0; i < 8; ++i)
			{
				Vector3I corner;
				corner.X = (i & 1) == 0 ? min.X : max.X;
				corner.Y = (i & 2) == 0 ? min.Y : max.Y;
				corner.Z = (i & 4) == 0 ? min.Z : max.Z;
				box.Include(grid.GridIntegerToWorld(corner));
			}

			// Probes reach one block past the range (face sub-cell, neighbour scan) plus the half-cell voxel sphere.
			box.Inflate(grid.GridSize + 2.0);
			return box;
		}

		public void QueryObstacles(BoundingBoxD worldBox, List<MyVoxelBase> voxels, List<IMyCubeGrid> grids)
		{
			MyGamePruningStructure.GetAllVoxelMapsInBox(ref worldBox, voxels);

			foreach (var entity in MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref worldBox))
			{
				var g = entity as IMyCubeGrid;
				if (g != null && g != grid && !Commands.IsProjection(g))
					grids.Add(g);
			}
		}

		public Traversability GetForAstar(Vector3I cell, List<MyVoxelBase> voxels, List<IMyCubeGrid> grids)
		{
			var blockIJK = cell >> resolutionBits;

			if (resolutionBits == 0)
				return GetTraversability(blockIJK, voxels, grids);

			var sub = cell & ((1 << resolutionBits) - 1);

			// Edges and corners: always blocked
			if (sub.X + sub.Y + sub.Z > 1)
				return Traversability.Blocked;

			// Center sub-cell: use existing traversability
			if (sub.X + sub.Y + sub.Z == 0)
				return GetTraversability(blockIJK, voxels, grids);

			// Face sub-cell: passable if both adjacent blocks' face probes are clear
			var travA = GetTraversability(blockIJK, voxels, grids);
			var travB = GetTraversability(blockIJK + sub, voxels, grids);

			if (travA[sub] || travB[-sub])
				return Traversability.Blocked;

			return Traversability.Free;
		}

		public Traversability GetTraversability(Vector3I position, List<MyVoxelBase> voxels, List<IMyCubeGrid> grids)
		{
			var t = Probe(position, voxels, grids);

			// The engineer already stands here, so a closed center is no reason to refuse to move.
			if (position == engineerCell) t[0, 0, 0] = false;

			return t;
		}

		private Traversability Probe(Vector3I position, List<MyVoxelBase> voxels, List<IMyCubeGrid> grids)
		{
			foreach(var voxel in voxels)
				if(!IsVoxelTraversable(voxel, position)) return Traversability.Blocked;

			var center = grid.GridIntegerToWorld(position);
			foreach (var g in grids)
				if (Collisions.CheckSphereVsGrid(g, center, Constants.CollisionProbeRadius))
					return Traversability.Blocked;

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
			{	BoundingSphereD sphere = new BoundingSphereD(vc, grid.GridSize * 0.5); // 0.2 // Very slow but precise
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
