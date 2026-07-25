using System;

using VRageMath;
using VRage.Game.ModAPI;
using VRage.Voxels;
using Sandbox.Game.Entities;

namespace LLE
{
	public static class SurfaceSampler
	{
		public static bool TryGetRandomSurfacePoint(MyVoxelBase voxel, Random random, out Vector3D worldPosition)
		{
			worldPosition = Vector3D.Zero;
			if (voxel == null || voxel.Storage == null) return false;

			var size = voxel.Storage.Size;
			int maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));
			if (maxDim <= 0) return false;

			int lod = Math.Max(0, (int)Math.Ceiling(Math.Log(maxDim, 2)) - 1);
			if (lod > 0) --lod;
			if (lod > 0) --lod;

			Vector3I parentMin = Vector3I.Zero;
			Vector3I parentMax = new Vector3I((size.X - 1) >> lod, (size.Y - 1) >> lod, (size.Z - 1) >> lod);

			var cache = new MyStorageData();
			var candidates = new Vector3I[8 * 8 * 8];

			while (true)
			{
				cache.Resize(parentMin, parentMax);
				voxel.Storage.ReadRange(cache, MyStorageDataTypeFlags.Content, lod, parentMin, parentMax);

				int count = 0;
				Vector3I local;
				var sizeLocal = parentMax - parentMin + 1;
				for (local.Z = 0; local.Z < sizeLocal.Z; local.Z++)
					for (local.Y = 0; local.Y < sizeLocal.Y; local.Y++)
						for (local.X = 0; local.X < sizeLocal.X; local.X++)
						{
							byte content = cache.Content(ref local);
							if (content > 0 && content < byte.MaxValue)
								candidates[count++] = parentMin + local;
						}
				if (count == 0) return false;

				var picked = candidates[random.Next(count)];
				if (lod == 0)
				{
					BoundingBoxD aabb;
					MyVoxelCoordSystems.VoxelCoordToWorldAABB(voxel.PositionLeftBottomCorner, ref picked, out aabb);
					worldPosition = aabb.Center;
					return true;
				}

				--lod;
				parentMin = picked << 1;
				parentMax = parentMin + 1;
				var cap = new Vector3I((size.X - 1) >> lod, (size.Y - 1) >> lod, (size.Z - 1) >> lod);
				parentMax = Vector3I.Min(parentMax, cap);
			}
		}

		public static bool TryGetRandomBlockOnSurface(IMyCubeGrid grid, Random random, out Vector3D worldPosition)
		{
			worldPosition = Vector3D.Zero;
			if (grid == null) return false;

			Vector3I min = grid.Min;
			Vector3I max = grid.Max;
			Vector3D center = 0.5 * (new Vector3D(min) + new Vector3D(max));

			// Random direction on unit sphere
			double z = random.NextDouble() * 2 - 1;
			double a = random.NextDouble() * 2 * Math.PI;
			double r = Math.Sqrt(Math.Max(0, 1 - z * z));
			Vector3D dir = new Vector3D(r * Math.Cos(a), r * Math.Sin(a), z);

			// Project ray (center + t*dir, t>0) onto AABB exit face.
			// Expand AABB by half a cell so the start point lies just outside the outer cell layer.
			Vector3D boxMin = new Vector3D(min) - 0.5;
			Vector3D boxMax = new Vector3D(max) + 0.5;

			Vector3D start;
			if(!ClipLineByBox(center, dir, boxMin, boxMax, out start)) return false;

			// March from surface point back to center, half-cell steps.
			Vector3D path = center - start;
			int steps = (int)Math.Ceiling(path.Length() * 2) + 1;
			Vector3D delta = path / steps;

			Vector3I prevCell = new Vector3I(int.MinValue, int.MinValue, int.MinValue);
			for (int i = 0; i <= steps; i++)
			{
				Vector3D p = start + delta * i;
				Vector3I cell = new Vector3I(
					(int)Math.Round(p.X),
					(int)Math.Round(p.Y),
					(int)Math.Round(p.Z));
				if (cell == prevCell) continue;
				prevCell = cell;

				var block = grid.GetCubeBlock(cell);
				if (block != null)
				{
					worldPosition = grid.GridIntegerToWorld(block.Position);
					return true;
				}
			}
			return false;
		}

		// assumes center starts inside (or on) the box
		private static bool ClipLineByBox(Vector3D center, Vector3D direction, Vector3D min, Vector3D max, out Vector3D start)
		{
			double exitAtLength = double.MaxValue;
			exitAtLength = ClipLength(exitAtLength, direction.X, center.X, min.X, max.X);
			exitAtLength = ClipLength(exitAtLength, direction.Y, center.Y, min.Y, max.Y);
			exitAtLength = ClipLength(exitAtLength, direction.Z, center.Z, min.Z, max.Z);
			if (exitAtLength == double.MaxValue)
			{	start = Vector3D.Zero;
				return false;
			}

			start = center + direction * exitAtLength;
			return true;
		}

		private static double ClipLength(double lowest, double direction, double center, double low, double high)
		{
			if (Math.Abs(direction) < 1e-9) return lowest;
			double t = ((direction > 0 ? high : low) - center) / direction;
			return (t > 0 && t < lowest) ? t : lowest;
		}
	}
}
