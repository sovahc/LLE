using System;
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

			if(!capsuleClear) return false;

			// Check if the engineer's stand position is blocked by a foreign grid.
			if (CheckWorldSphereAgainstGrids(a, Constants.CollisionProbeRadius, grid))
				return false;

			return true;
		}

		/// <summary>
		/// Checks if a world-space sphere intersects any cube grid except ignoreGrid.
		/// Broad phase: entity AABB overlap via GetTopMostEntitiesInSphere.
		/// Narrow phase: CheckWorldSphere per candidate block.
		/// </summary>
		public static bool CheckWorldSphereAgainstGrids(Vector3D worldCenter, double radius, IMyCubeGrid ignoreGrid)
		{
			var sphere = new BoundingSphereD(worldCenter, radius);
			var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

			bool blocked = false;

			foreach (var entity in entities)
			{
				var grid = entity as IMyCubeGrid;
				if (grid == null) continue;
				if (grid == ignoreGrid) continue;

				// Sphere is rotation-invariant: center transforms, radius stays.
				MatrixD invWorld = grid.PositionComp.WorldMatrixNormalizedInv;
				Vector3D localCenter = Vector3D.Transform(worldCenter, invWorld);
				float gridSizeR = 1f / grid.GridSize;

				// Conservative cell range: Floor/Ceiling, CheckWorldSphere is the real filter.
				Vector3I min = new Vector3I(
					(int)Math.Floor((localCenter.X - radius) * gridSizeR),
					(int)Math.Floor((localCenter.Y - radius) * gridSizeR),
					(int)Math.Floor((localCenter.Z - radius) * gridSizeR));
				Vector3I max = new Vector3I(
					(int)Math.Ceiling((localCenter.X + radius) * gridSizeR),
					(int)Math.Ceiling((localCenter.Y + radius) * gridSizeR),
					(int)Math.Ceiling((localCenter.Z + radius) * gridSizeR));

				var iter = new Vector3I_RangeIterator(ref min, ref max);
				for (; iter.IsValid(); iter.MoveNext())
				{
					var block = grid.GetCubeBlock(iter.Current);
					if (block == null) continue;

					if (Collisions.CheckWorldSphere(block, worldCenter, radius))
					{
						blocked = true;
						goto done;
					}
				}
			}

		done:
			Drawing.ScreenSphere(worldCenter, (float)radius, (blocked ? Color.Gray : Color.Lime).ToVector4());
			return blocked;
		}

		static readonly List<Vector3I> candidateCells = new List<Vector3I>();

		/// <summary>
		/// Orders candidate cells by distance from the block's geometric center (grid space).
		/// A small off-axis jitter breaks ties for symmetric blocks.
		/// </summary>
		static void OrderCandidatesByDistance(List<Vector3I> cells, IMySlimBlock block)
		{
			// Grid-space center of the block
			Vector3D center = (block.Min + block.Max) * 0.5;
			center -= new Vector3D(0.1, 0.1, 0.1); // breaks symmetry

			cells.Sort((a, b) =>
			{
				var da = (new Vector3D(a.X, a.Y, a.Z) - center).LengthSquared();
				var db = (new Vector3D(b.X, b.Y, b.Z) - center).LengthSquared();
				return da.CompareTo(db);
			});
		}

		public static void Query(IMySlimBlock block, Vector3D engineerPosition, List<EQSResult> results)
		{
			results.Clear();

			var grid = block.CubeGrid;

			var min = block.Min - 1;
			var max = block.Max + 1;

			candidateCells.Clear();
			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if (ijkBlock != null && !Collisions.CenterIsFree(ijkBlock, ijk)) continue;

				candidateCells.Add(ijk);
			}

			OrderCandidatesByDistance(candidateCells, block);

			const int maxResults = 5;

			foreach (var ijk in candidateCells)
			{
				Vector3D worldPos = grid.GridIntegerToWorld(ijk);

				Vector3D collisionCenter;
				Collisions.GetNearestCollisionCenter(block, worldPos, out collisionCenter);

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

				if (results.Count >= maxResults) break;
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