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

		public static void Initialize()
		{
			var h2 = Constants.EngineerCapsuleHeight / 2;

			// Capsule: axis along local +Y (up)
			Geometry.CapsuleToConvex(
				new Vector3(0, -h2, 0),
				new Vector3(0, +h2, 0),
				Constants.EngineerCapsuleRadius, capsuleModel);
		}

		public static bool IsGoodPosition(Vector3D standPos, Vector3D collisionCenter, IMySlimBlock targetBlock,
			out Vector3D forward, out Vector3D up)
		{
			forward = Vector3D.Zero;
			up = Vector3D.Zero;

			var grid = targetBlock.CubeGrid;
			var gridUp = grid.WorldMatrix.Up;

			var h2 = Constants.EngineerCapsuleHeight / 2;
			var eyePos = standPos + gridUp * h2;

			forward = collisionCenter - eyePos;
			double dist = forward.Length();
			if (dist < 0.1) return false;
			forward /= dist;

			// Up perpendicular to forward, closest to grid up.
			// Skip when forward is nearly parallel to grid up (engineer can't look straight up/down).
			var right = Vector3D.Cross(gridUp, forward);
			if (right.LengthSquared() < 1e-10) return false;

			var world = MatrixD.CreateWorld(standPos, forward, gridUp);
			up = world.Up;

			// Raycast from eye toward collision center
			var a = eyePos;
			var b = collisionCenter;

			IHitInfo hitInfo;
			MyAPIGateway.Physics.CastRay(a, b, out hitInfo, CollisionLayers.CollisionLayerWithoutCharacter);

			bool hit = false;
			if (hitInfo != null)
			{
				var hitPos = a + (b - a) * hitInfo.Fraction * 1.01;
				b = hitPos;
				var hitIJK = grid.WorldToGridInteger(hitPos);
				var hitBlock = grid.GetCubeBlock(hitIJK);
				if (hitBlock == targetBlock) hit = true;
			}

			if (!hit) return false;

			var dir = (b - a).Normalized();
			a = b - dir * Constants.GrindWeldDistance;
			world.Translation = a;

			var capsule = new List<Vector3D>(capsuleModel.Count);
			for (int i = 0; i < capsuleModel.Count; i++)
				capsule.Add(Vector3D.Transform(capsuleModel[i], world));

			Vector3I capMin, capMax;
			MinMax(grid, capsule, out capMin, out capMax);
			bool capsuleClear = !Collisions.ConvexVsGridGeometry(grid, capsule, capMin, capMax, null);

			var material = MyStringId.GetOrCompute("Square");
			var color = capsuleClear ? Color.Cyan.ToVector4() : Color.DarkGray.ToVector4();
			MySimpleObjectDraw.DrawLine(a, b, material, ref color, 0.01f);
			Drawing.ConvexOutline(capsule, 1e-4f, capsuleClear ? Color.Cyan : Color.DarkGray);

			return capsuleClear;
		}

		public static void Query(IMySlimBlock block, Vector3D engineerPosition, List<EQSResult> results)
		{
			results.Clear();

			var grid = block.CubeGrid;

			//Collisions.GetCollisionCenters(block, collisionCenters);
			//if (collisionCenters.Count == 0) return;

			var min = block.Min - 1;
			var max = block.Max + 1;

			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if (ijkBlock != null && !Collisions.CenterIsFree(ijkBlock, ijk)) continue;

				Vector3D worldPos = grid.GridIntegerToWorld(ijk);

				//foreach (var collisionCenter in collisionCenters)
				Vector3D collisionCenter;
				Collisions.GetNearestCollisionCenter(block, worldPos, out collisionCenter);
				{
					Vector3D forward, up;
					if (!IsGoodPosition(worldPos, collisionCenter, block, out forward, out up)) continue;

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