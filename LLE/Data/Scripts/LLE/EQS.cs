using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	public static class EQS
	{
		static List<Vector3> capsuleModel = new List<Vector3>();
		static List<Vector3> cylinderModel = new List<Vector3>();

		public static void Initialize()
		{
			// Capsule: axis along local +Y (head up), from feet (-height) to head (0).
			Geometry.CapsuleToConvex(
				new Vector3(0, -Constants.EngineerCapsuleHeight, 0),
				Vector3.Zero,
				Constants.EngineerCapsuleRadius, capsuleModel);

			// Cylinder: axis along local -Z (head forward), from head (0) to reach.
			Geometry.CylinderToConvex(
				Vector3.Zero,
				new Vector3(0, 0, -Constants.MaxInteractionDistance / 2), // ??
				0.05f, cylinderModel);
		}

		public static bool IsGoodPosition(Vector3D p, Vector3D fwd, Vector3D up, IMySlimBlock targetBlock)
		{
			var grid = targetBlock.CubeGrid;

			// Shapes are built in model space (float, near origin) then transformed to
			// world space (double) so large world coordinates aren't truncated to float.
			var world = MatrixD.CreateWorld(p, fwd, up);

			var capsule = new List<Vector3D>(capsuleModel.Count);
			for (int i = 0; i < capsuleModel.Count; i++)
				capsule.Add(Vector3D.Transform(capsuleModel[i], world));

			var cylinder = new List<Vector3D>(cylinderModel.Count);
			for (int i = 0; i < cylinderModel.Count; i++)
				cylinder.Add(Vector3D.Transform(cylinderModel[i], world));

			Vector3I capMin, capMax;
			MinMax(grid, capsule, out capMin, out capMax);
			bool capsuleClear = !Collisions.ConvexVsGridGeometry(grid, capsule, capMin, capMax, null);

			Drawing.ConvexOutline(capsule, 1e-4f, capsuleClear ? Color.Green : Color.Gray);

			if (!capsuleClear) return false;

			var cylIntersected = new List<IMySlimBlock>();
			Vector3I cylMin, cylMax;
			MinMax(grid, cylinder, out cylMin, out cylMax);
			Collisions.ConvexVsGridGeometry(grid, cylinder, cylMin, cylMax, cylIntersected);

			bool cylinderGood = cylIntersected.Count == 1 && cylIntersected[0] == targetBlock;

			Drawing.ConvexOutline(cylinder, 1e-4f, cylinderGood ? Color.Green : Color.Gray);

			return cylinderGood;
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