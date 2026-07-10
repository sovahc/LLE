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
		public Vector3I Cell;
		public Vector3D chPosition;
		public Vector3D chUp;
		public Vector3D Target;
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

		enum InteractionKind
		{	GrindWeld,
			Inventory,
			Recharge			
		}

//EQS.Query
// ├─ EnumerateCandidateCells
// ├─ Collisions.TryGetVisibleInteraction
// ├─ TryCreatePose
// └─ IsPoseClear

		private static Vector3D? CalculateInteractionPoint_Collision(IMySlimBlock block, Vector3D worldFrom)
		{
			var a = worldFrom;
			Vector3D b;

			if(!Collisions.HasCollision(block))
			{	// light, camera, e.t.c.
				var fat = block.FatBlock;
				if (fat == null) return null;
				
				b = fat.PositionComp.WorldAABB.Center;
				return b;
			}

			if(block.Min != block.Max)
			{	/// nearest cell??				
			}

			b = Collisions.GetGrindWeldTarget(block, a);

			IHitInfo hitInfo;
			MyAPIGateway.Physics.CastRay(a, b, out hitInfo, CollisionLayers.CollisionLayerWithoutCharacter);

			if (hitInfo == null) return null;

			var grid = block.CubeGrid;
				
			var hit = a + (b - a) * hitInfo.Fraction * 1.01;
			var hitIJK = grid.WorldToGridInteger(hit);
			var hitBlock = grid.GetCubeBlock(hitIJK);
			if (hitBlock != block) return null;

			return hit;
		}

		private static Vector3D? CalculateInteractionPoint_Detectors(
			IMySlimBlock block, Vector3D worldFrom, InteractionKind kind)
		{	
			CollisionGeometry geometry;
			if (!Collisions._collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry))
				return null; // cannot interact with unknown block

			var grid = block.CubeGrid;
			Vector3 modelFrom = Transform.WorldToModel(block, worldFrom);

			Vector3D? result = null;
			float minDistance = float.MaxValue;

			foreach (var detector in geometry.Detectors)
			{	
				if(kind == InteractionKind.Inventory)
				{	bool inventory =
						detector.Name.StartsWith("conveyor_") ||
						detector.Name.StartsWith("inventory_") || 
						detector.Name.StartsWith("cockpit_");
					if(!inventory) continue;
				}
				if(kind == InteractionKind.Recharge)
				{	bool medblock = detector.Name.StartsWith("block_");
					if(!medblock) continue;
				}

				var detectorCenter = detector.Transform.Translation;
				var dcWordld = Transform.ModelToWorld(block, detectorCenter);
				Drawing.RoundMarker(dcWordld, Color.Purple);

				var line = new Line(modelFrom, detectorCenter);
					
				if(line.Length > Constants.MaxInteractionDistance) continue;

				var intersection = Collisions.RaycastDetector(detector, line);
				if(!intersection.HasValue) continue;

				var clippedByDetector = new Line(line.From, line.From + line.Direction * intersection.Value);
				var worldLine = new LineD(worldFrom, Transform.ModelToWorld(block, clippedByDetector.To));

				var min = block.Min-1; // XXX big query
				var max = block.Max+1;
				
				bool ligg = Collisions.LineIntersectsGridGeometry(grid, worldLine, min, max, null);
				
				if(ligg) continue;

				if(intersection.Value < minDistance)
				{	minDistance = intersection.Value;
					result = worldLine.To;
				}
			}
			return result;
		}

		private static bool IsGoodPosition(Vector3D gridUp,
			Vector3D engineerCenter, Vector3D target, IMySlimBlock targetBlock,
			out Vector3D position, out Vector3D forward, out Vector3D up)
		{
			position = Vector3D.Zero;
			forward = Vector3D.Zero;
			up = Vector3D.Zero;

			var grid = targetBlock.CubeGrid;

			var h2 = Constants.EngineerCapsuleHeight / 2;
			var eyePosition = engineerCenter + gridUp * h2;

			forward = (target - eyePosition).Normalized();

			var world = MatrixD.CreateWorld(engineerCenter, forward, gridUp);
			up = world.Up;

			// Raycast from eye toward collision center
			var a = eyePosition;
			var b = target;

			//var rc = RaycastForGrindWeld(a, b, targetBlock);
			var rc = CalculateInteractionPoint_Detectors(targetBlock, a, InteractionKind.Inventory);

			if (rc == null) return false;
			b = rc.Value;

			var material = MyStringId.GetOrCompute("Square");
			var color = Color.Cyan.ToVector4();
			MySimpleObjectDraw.DrawLine(a, b, material, ref color, 0.01f);

			var dir = (b - a).Normalized();
			a = b - dir * Constants.GrindWeldDistance;
			
			position = a - up * h2;
			world.Translation = position;

			var capsule = new List<Vector3D>(capsuleModel.Count);
			for (int i = 0; i < capsuleModel.Count; i++)
				capsule.Add(Vector3D.Transform(capsuleModel[i], world));

			Vector3I capMin, capMax;
			MinMax(grid, capsule, out capMin, out capMax);
			bool capsuleClear = !Collisions.ConvexVsGridGeometry(grid, capsule, capMin, capMax, null);

			Drawing.ConvexOutline(capsule, 1e-4f, capsuleClear ? Color.Cyan : Color.DarkGray);

			if(!capsuleClear) return false;

			// Check if the engineer's stand position is blocked by a foreign grid.
			if (Collisions.CheckWorldSphereAgainstGrids(position, Constants.CollisionProbeRadius, grid))
				return false;

			return true;
		}

		public static void Query(IMySlimBlock block, Vector3D engineerPosition,
			List<EQSResult> results, int maxResults)
		{	var min = block.Min - 1;
			var max = block.Max + 1;

			Query(block, min, max, engineerPosition, results, maxResults);
		}

		public static void QueryOneCell(IMySlimBlock block, Vector3I cell, Vector3D engineerPosition,
			List<EQSResult> results, int maxResults)
		{	Query(block, cell, cell, engineerPosition, results, maxResults);
		}

		private static void Query(IMySlimBlock block, Vector3I min, Vector3I max,
			Vector3D engineerPosition, List<EQSResult> results, int maxResults)
		{
			Debug.ClearLines();

			results.Clear();

			var grid = block.CubeGrid;

			if(!Collisions.HasCollision(block))
			{	// light, camera, e.t.c.
				min = block.Min;
				max = block.Max;
			}

			var gridUp = Commands.CalculateUpVector(grid);

			var producer = ProduceCells(block, min, max, engineerPosition);

			foreach (var ijk in producer)
			{
				Vector3D ijkWorld = grid.GridIntegerToWorld(ijk);
				Vector3D? r;
				r = CalculateInteractionPoint_Collision(block, ijkWorld);
				if(r.HasValue) Debug.AddLine(new LineD(ijkWorld, r.Value), Color.Red);

				r = CalculateInteractionPoint_Detectors(block, ijkWorld, InteractionKind.Inventory);
				if(r.HasValue) Debug.AddLine(new LineD(ijkWorld, r.Value), Color.Yellow);

				r = CalculateInteractionPoint_Detectors(block, ijkWorld, InteractionKind.Recharge);
				if(r.HasValue) Debug.AddLine(new LineD(ijkWorld, r.Value), Color.White);
				
				//Vector3D target = Collisions.GetGrindWeldTarget(block, ijkWorld);
				//Vector3D target;
				//if(!Collisions.GetNearestDetectorCenterByPrefix(block, engineerPosition, "conveyor_", out target))
				//	continue;

				//Vector3D position, forward, up;
				//if (!IsGoodPosition(gridUp, ijkWorld, target, block, out position, out forward, out up)) continue;

				//results.Add(new EQSResult
				//{
				//	Cell = ijk,
				//	chPosition = position,
				//	chUp = up,
				//	Target = target
				//});
				//
				//if (results.Count >= maxResults) break;
			}
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

		public static IEnumerable<Vector3I> ProduceCells(IMySlimBlock block, Vector3I min, Vector3I max,
			Vector3D engineerPosition)
		{
			var grid = block.CubeGrid;
			
			List<Vector3I> candidates = new List<Vector3I>();

			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if (ijkBlock != null && !Collisions.CenterIsFree(ijkBlock, ijk)) continue;

				candidates.Add(ijk);
			}

			// reorder

			Vector3D blockCenter = (block.Min + block.Max) * 0.5;

			var invWorld = grid.PositionComp.WorldMatrixNormalizedInv;
			Vector3D engineerCell = grid.WorldToGridInteger(engineerPosition);
			Vector3D toEngineer = engineerCell - blockCenter;
			
			var shiftedCenter = blockCenter + toEngineer.Normalized() * 0.1; // Break tie

			candidates.Sort((a, b) =>
			{
				var da = (new Vector3D(a) - shiftedCenter).LengthSquared();
				var db = (new Vector3D(b) - shiftedCenter).LengthSquared();
				return da.CompareTo(db);
			});

			// output

			foreach (var ijk in candidates) yield return ijk;
		}
	}
}
