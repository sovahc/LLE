using System;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
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
		static readonly List<Vector3D> tmp = new List<Vector3D>();

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
			if(block.CubeGrid.Physics == null)
			{	// projected block: not worth navigating to at all if it can't actually be built yet
				var mcg = block.CubeGrid as MyCubeGrid;
				var projector = mcg?.Projector as IMyProjector;
				if(projector == null || projector.CanBuild(block, true) != BuildCheckResult.OK)
					return null;
			}

			var a = worldFrom;
			Vector3D b;

			if(!Collisions.HasCollision(block))
			{	// light, camera, etc.
				var fat = block.FatBlock;
				if (fat == null) return null;

				b = fat.PositionComp.WorldAABB.Center;
				return b;
			}

			if(block.Min != block.Max)
			{	/// nearest cell??
			}

			b = Collisions.GetGrindWeldTarget(block, a);

			if(block.CubeGrid.Physics == null)
				// projector preview block: no physics body to raycast against, clip to the block's own bounding box instead
				return ClipToBlockBoundingBox(block, a, b);

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

		// Crude interaction point when there's no physics to raycast against (projector previews):
		// clip the line from the block center toward the viewer against the block's own local
		// bounding box, in the block's model space (so grid/block rotation is accounted for).
		private static Vector3D ClipToBlockBoundingBox(IMySlimBlock block, Vector3D worldFrom, Vector3D worldCenter)
		{
			var modelFrom = Transform.WorldToModel(block, worldFrom);
			var modelCenter = Transform.WorldToModel(block, worldCenter);

			Vector3D boxMin, boxMax;

			CollisionGeometry geometry;
			Vector3 localMin, localMax;
			if (Collisions._collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry) &&
				Collisions.GetLocalBounds(geometry, out localMin, out localMax))
			{	// shape bounds are already in the block's model space (see PreprocessCG) — same space as modelFrom/modelCenter
				boxMin = new Vector3D(localMin);
				boxMax = new Vector3D(localMax);
			}
			else
			{	// no known geometry: fall back to the block's cell footprint (grid-axis-aligned, exact only for single-cell blocks)
				double cellSize = block.CubeGrid.GridSize;
				var halfExtents = new Vector3D(block.Max - block.Min + Vector3I.One) * cellSize * 0.5;
				boxMin = -halfExtents;
				boxMax = halfExtents;
			}

			Vector3D clipped;
			if (!Intersections.ClipLineByBox(modelCenter, modelFrom - modelCenter, boxMin, boxMax, out clipped))
				return worldCenter;

			return Transform.ModelToWorld(block, clipped);
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
				var dcWorld = Transform.ModelToWorld(block, detectorCenter);
				Drawing.RoundMarker(dcWorld, Color.Purple);

				var line = new Line(modelFrom, detectorCenter);
					
				if(line.Length > Constants.MaxInteractionDistance) continue;

				var intersection = Collisions.RaycastDetector(detector, line);
				if(!intersection.HasValue) continue;

				var clippedByDetector = new Line(line.From, line.From + line.Direction * intersection.Value);
				var worldLine = new LineD(worldFrom, Transform.ModelToWorld(block, clippedByDetector.To));

				IHitInfo hitInfo;
				if(MyAPIGateway.Physics.CastRay(worldLine.From, worldLine.To, out hitInfo,
					CollisionLayers.CollisionLayerWithoutCharacter)) continue;

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
			{	// light, camera, etc.
				min = block.Min;
				max = block.Max;
			}

			var gridUp = Commands.CalculateUpVector(grid);

			var producer = ProduceCells(block, min, max, engineerPosition);

			Vector3D blockCenterWorld = (grid.GridIntegerToWorld(block.Min) + grid.GridIntegerToWorld(block.Max)) * 0.5;
			var horizontalEngineer = blockCenterWorld - engineerPosition;
			horizontalEngineer -= gridUp * Vector3D.Dot(horizontalEngineer, gridUp);
			if (horizontalEngineer.LengthSquared() < 1e-8)
				horizontalEngineer = Vector3D.CalculatePerpendicularVector(gridUp);
			else
				horizontalEngineer.Normalize();

			foreach (var ijk in producer)
			{
				Vector3D ijkWorld = grid.GridIntegerToWorld(ijk);
				Vector3D ijkDirection = (ijkWorld-blockCenterWorld).Normalized();

				Vector3D up;
				
				var dot = Math.Abs(Vector3D.Dot(ijkDirection, gridUp));
				if(dot < 0.7)
				{	up = gridUp;					
				}
				else
				{	//Debug.AddLine(new LineD(ijkWorld, ijkWorld+horizontalEngineer), Color.Cyan);
					up = horizontalEngineer;
				}

				var eyesShift = up * Constants.EngineerCapsuleHeight / 2;

				var worldFrom = ijkWorld + eyesShift;

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
				//else
				//	Drawing.RoundMarker(worldFrom, Color.Black);

				if(!r.HasValue) continue;

				// check engineer placement
				
				var worldTo = r.Value;
				var direction = (worldTo - worldFrom).Normalized();

				var desiredDist = (kind == InteractionKind.GrindWeld) ?
					Constants.MaxGrindWeldDistance :
					Constants.MaxInteractionDistance;

				var naturalDist = (worldTo - worldFrom).Length();

				if(desiredDist > naturalDist) desiredDist = (float)naturalDist;

				worldFrom = worldTo - direction * desiredDist;
				worldFrom -= eyesShift;

				var world = MatrixD.CreateWorld(worldFrom, worldTo-worldFrom, up); // normalization is inside.

				tmp.Clear();
				var capsule = tmp;
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

			if(grid.GridSizeEnum == MyCubeSize.Large)
			{	var iter = new Vector3I_RangeIterator(ref min, ref max);
				for (; iter.IsValid(); iter.MoveNext())
				{
					var ijk = iter.Current;

					var ijkBlock = grid.GetCubeBlock(ijk);
					if (ijkBlock != null && !Collisions.CenterIsFree(ijkBlock, ijk)) continue;

					candidates.Add(ijk);
				}
			}

			Vector3D blockCenter = (block.Min + block.Max) * 0.5;

			if(grid.GridSizeEnum == MyCubeSize.Small)
			{
				Vector3I v;

				for(v.Z = -1; v.Z <= 1; ++v.Z)
					for(v.Y = -1; v.Y <= 1; ++v.Y)
						for(v.X = -1; v.X <= 1; ++v.X)
						{
							if(v == Vector3I.Zero) continue;

							var offset = blockCenter + new Vector3D(v) * 5.1; // 5 small cells = 1 large cell
							var ijk = new Vector3I(offset);

							candidates.Add(ijk);
						}
			}

			// reorder

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
