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
	internal struct EQSResult
	{
		public Vector3I Cell;
		public Vector3D chPosition;
		public Vector3D chUp;
		public Vector3D Target;
	}

	internal enum InteractionKind
	{	GrindWeld,
		Inventory,
		Recharge			
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

		internal static void Query(IMySlimBlock block, Vector3D engineerPosition, InteractionKind kind,
			List<EQSResult> results, int maxResults)
		{	var min = block.Min - 1;
			var max = block.Max + 1;

			Query(block, min, max, engineerPosition, kind, results, maxResults);
		}

		internal static void QueryOneCell(IMySlimBlock block, Vector3I cell,
			Vector3D engineerPosition, InteractionKind kind,
			List<EQSResult> results, int maxResults)
		{	Query(block, cell, cell, engineerPosition, kind, results, maxResults);
		}

		private static void Query(IMySlimBlock block, Vector3I min, Vector3I max,
			Vector3D engineerPosition, InteractionKind kind,
			List<EQSResult> results, int maxResults)
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

				var worldFrom = ijkWorld + gridUp * Constants.EngineerCapsuleHeight / 2; // XXxx

				Vector3D? r = null;

				switch(kind)
				{	case InteractionKind.GrindWeld:
						r = CalculateInteractionPoint_Collision(block, worldFrom);
						break;
					case InteractionKind.Inventory:
						r = CalculateInteractionPoint_Detectors(block, worldFrom, InteractionKind.Inventory);
						break;
					case InteractionKind.Recharge:
						r = CalculateInteractionPoint_Detectors(block, worldFrom, InteractionKind.Recharge);
						break;
				}
				if(r.HasValue)
					Debug.AddLine(new LineD(worldFrom, r.Value), Color.Green);
				else
					Drawing.RoundMarker(worldFrom, Color.Black);

				if(!r.HasValue) continue;

				// check engineer placement

				var worldTo = r.Value;

				var world = MatrixD.CreateWorld(worldFrom, worldTo-worldFrom, gridUp); // normailization is inside.

				var capsule = new List<Vector3D>(capsuleModel.Count);
				for (int i = 0; i < capsuleModel.Count; i++)
					capsule.Add(Vector3D.Transform(capsuleModel[i], world));
				
				Vector3I capMin, capMax;
				MinMax(grid, capsule, out capMin, out capMax);
				bool capsuleClear = !Collisions.ConvexVsGridGeometry(grid, capsule, capMin, capMax, null);

				Drawing.ConvexOutline(capsule, 1e-4f, capsuleClear ? Color.Cyan : Color.DarkGray);

				if(!capsuleClear) continue;
				
				results.Add(new EQSResult
				{
					Cell = ijk,
					chPosition = worldFrom,
					chUp = world.Up,
					Target = worldTo
				});
				
				if (results.Count >= maxResults) break;
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
