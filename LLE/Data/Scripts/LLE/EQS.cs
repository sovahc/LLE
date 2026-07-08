using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

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
		static Line raycast;

		public static void Initialize()
		{
			var h2 = Constants.EngineerCapsuleHeight / 2;

			// Capsule: axis along local +Y (up)
			Geometry.CapsuleToConvex(
				new Vector3(0, -h2, 0),
				new Vector3(0, +h2, 0),
				Constants.EngineerCapsuleRadius, capsuleModel);

			raycast = new Line(new Vector3(0, +h2, 0), new Vector3(0, +h2, -Constants.MaxInteractionDistance / 2));
		}

		public static bool IsGoodPosition(Vector3D p, Vector3D forward, Vector3D up, IMySlimBlock targetBlock)
		{
			var grid = targetBlock.CubeGrid;

			var world = MatrixD.CreateWorld(p, forward, up);

			var a = Vector3D.Transform(raycast.From, world);
			var b = Vector3D.Transform(raycast.To, world);

			IHitInfo hitInfo;
			MyAPIGateway.Physics.CastRay(a, b, out hitInfo, CollisionLayers.CollisionLayerWithoutCharacter);

			bool hit = false;
			if(hitInfo != null)
			{	b = a + (b - a) * hitInfo.Fraction * 1.01;
				var hitIJK = grid.WorldToGridInteger(b);
				var hitBlock = grid.GetCubeBlock(hitIJK);
				if(hitBlock == targetBlock) hit = true;
			}

			var material = MyStringId.GetOrCompute("Square");
			var color = hit ? Color.Plum.ToVector4() : Color.DarkGray.ToVector4();
			MySimpleObjectDraw.DrawLine(a, b, material, ref color, 0.01f);
			
			if(!hit) return false;

			var capsule = new List<Vector3D>(capsuleModel.Count);
			for (int i = 0; i < capsuleModel.Count; i++)
				capsule.Add(Vector3D.Transform(capsuleModel[i], world));

			Vector3I capMin, capMax;
			MinMax(grid, capsule, out capMin, out capMax);
			bool capsuleClear = !Collisions.ConvexVsGridGeometry(grid, capsule, capMin, capMax, null);

			Drawing.ConvexOutline(capsule, 1e-4f, capsuleClear ? Color.Cyan : Color.DarkGray);

			return capsuleClear;
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
					//if(grid.GetCubeBlock(ijk + dir) != block) continue;
					
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