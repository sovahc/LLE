using System;
using Sandbox.Game.Entities;
using VRage.Voxels;
using VRageMath;

namespace LLE
{
	public static class VoxelSurfaceSampler
	{
		public static bool TryGetRandomSurfacePoint(MyVoxelBase voxel, Random random, out Vector3D worldPosition)
		{
			worldPosition = Vector3D.Zero;
			if (voxel == null || voxel.Storage == null) return false;

			var size = voxel.Storage.Size;
			int maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));
			if (maxDim <= 0) return false;

			int lod = Math.Max(0, (int)Math.Ceiling(Math.Log(maxDim, 2)) - 1);
			if(lod > 0) --lod;
			if(lod > 0) --lod;

			Vector3I parentMin = Vector3I.Zero;
			Vector3I parentMax = new Vector3I((size.X - 1) >> lod, (size.Y - 1) >> lod, (size.Z - 1) >> lod);

			var cache = new MyStorageData();
			var candidates = new Vector3I[8*8*8];

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
	}
}
