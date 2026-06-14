using System;
using System.Text;
using System.Collections.Generic;

using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

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

		public TraversabilityCalculator(IMyCubeGrid grid_, int border_)
		{
			grid = grid_;
			border = border_;

			var worldAabb = grid.PositionComp.WorldAABB;
			intersectingVoxels.Clear();
			MyGamePruningStructure.GetAllVoxelMapsInBox(ref worldAabb, intersectingVoxels);
		}

		public Traversability GetForAstar(Vector3I astarPosition)
		{
			var position = astarPosition + grid.Min - border;
			return GetTraversability(position);
		}

		public Traversability GetTraversability(Vector3I position)
		{
			foreach(var voxel in intersectingVoxels)
				if(!IsVoxelTraversable(voxel, position)) return Traversability.Blocked;

			var slim = grid.GetCubeBlock(position);
			if (slim == null)
				return Traversability.Free;

			return Collisions.GetBlockTraversability(slim, position);
		}

		private bool IsVoxelTraversable(MyVoxelBase voxel, Vector3I gridPosition)
		{
			var v1 = grid.GridIntegerToWorld(gridPosition);
			var v2 = grid.GridIntegerToWorld(gridPosition+1);

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

		// Credit: Adapted from AI Enabled mod
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
