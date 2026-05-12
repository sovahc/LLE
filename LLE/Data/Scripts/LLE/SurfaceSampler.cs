using System;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using VRage.Voxels;
using VRageMath;

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
				MyConsole.Add($"{sizeLocal} {count}", Color.White);
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

			double tExit = double.MaxValue;
			tExit = NearestPositive(tExit, dir.X, center.X, boxMin.X, boxMax.X);
			tExit = NearestPositive(tExit, dir.Y, center.Y, boxMin.Y, boxMax.Y);
			tExit = NearestPositive(tExit, dir.Z, center.Z, boxMin.Z, boxMax.Z);
			if (tExit == double.MaxValue) return false;

			Vector3D start = center + dir * tExit;

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

		private static double NearestPositive(double best, double d, double c, double lo, double hi)
		{
			if (Math.Abs(d) < 1e-9) return best;
			double t = ((d > 0 ? hi : lo) - c) / d;
			return (t > 0 && t < best) ? t : best;
		}
	}
}
