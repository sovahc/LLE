using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	public struct EQSResult
	{
		public Vector3D Position;
		public Vector3D Forward;
		public Vector3D Up;
		public double Score;
	}

	public static class EQS
	{
		static readonly List<Vector3> capsuleModel = new List<Vector3>();
		static readonly List<Vector3> cylinderModel = new List<Vector3>();

		public static void Initialize()
		{
			var h2 = Constants.EngineerCapsuleHeight / 2;

			// Capsule: axis along local +Y (up)
			Geometry.CapsuleToConvex(
				new Vector3(0, -h2, 0),
				new Vector3(0, +h2, 0),
				Constants.EngineerCapsuleRadius, capsuleModel);

			// Cylinder: axis along local -Z (forward)
			Geometry.CylinderToConvex(
				new Vector3(0, +h2, 0),
				new Vector3(0, +h2, -Constants.MaxInteractionDistance / 2), // ??
				0.05f, cylinderModel);
		}

		public static bool IsGoodPosition(Vector3D p, Vector3D forward, Vector3D up, IMySlimBlock targetBlock)
		{
			var grid = targetBlock.CubeGrid;

			// Shapes are built in model space (float, near origin) then transformed to
			// world space (double) so large world coordinates aren't truncated to float.
			var world = MatrixD.CreateWorld(p, forward, up);

			var capsule = new List<Vector3D>(capsuleModel.Count);
			for (int i = 0; i < capsuleModel.Count; i++)
				capsule.Add(Vector3D.Transform(capsuleModel[i], world));

			var cylinder = new List<Vector3D>(cylinderModel.Count);
			for (int i = 0; i < cylinderModel.Count; i++)
				cylinder.Add(Vector3D.Transform(cylinderModel[i], world));

			Vector3I capMin, capMax;
			MinMax(grid, capsule, out capMin, out capMax);
			bool capsuleClear = !Collisions.ConvexVsGridGeometry(grid, capsule, capMin, capMax, null);

			if (!capsuleClear) return false;

			var cylIntersected = new List<IMySlimBlock>();
			Vector3I cylMin, cylMax;
			MinMax(grid, cylinder, out cylMin, out cylMax);
			Collisions.ConvexVsGridGeometry(grid, cylinder, cylMin, cylMax, cylIntersected);

			bool cylinderGood = cylIntersected.Count == 1 && cylIntersected[0] == targetBlock;

			Drawing.ConvexOutline(capsule, 1e-4f, cylinderGood ? Color.Green : Color.Gray);
			Drawing.ConvexOutline(cylinder, 1e-4f, cylinderGood ? Color.Green : Color.Gray);

			return cylinderGood;
		}

		public static void Query(IMySlimBlock block, Vector3D engineerPosition, List<EQSResult> results)
		{
			results.Clear();

			var grid = block.CubeGrid;

			var min = block.Min - 1;
			var max = block.Max + 1;

			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if (ijkBlock != null && !Collisions.CenterIsFree(ijkBlock, ijk)) continue;

				Vector3D worldPos = grid.GridIntegerToWorld(ijk);

				foreach (var dir in Constants.SixDirections)
				{
					if(grid.GetCubeBlock(ijk + dir) != block) continue;
					
					Vector3D forward = Vector3D.TransformNormal(new Vector3D(dir), grid.WorldMatrix);

					// Forward and Up must be perpendicular for MatrixD.CreateWorld.
					// Vertical forward (±Y) is parallel to grid up, so use a horizontal up instead.
					Vector3D up = (dir.Y != 0) ? grid.WorldMatrix.Forward : grid.WorldMatrix.Up;

					if (!IsGoodPosition(worldPos, forward, up, block)) continue;

					double score = -Vector3D.Distance(engineerPosition, worldPos);
					results.Add(new EQSResult
					{
						Position = worldPos,
						Forward = forward,
						Up = up,
						Score = score
					});
				}
			}

			results.Sort((a, b) => b.Score.CompareTo(a.Score));
		}

		private static void MinMax(IMyCubeGrid grid, List<Vector3D> world, out Vector3I min, out Vector3I max)
		{
			min = grid.WorldToGridInteger(world[0]);
			max = min;
			for (int v = 1; v < world.Count; v++)
			{
				var vp = grid.WorldToGridInteger(world[v]);
				min = Vector3I.Min(min, vp);
				max = Vector3I.Max(max, vp);
			}
		}
	}
}