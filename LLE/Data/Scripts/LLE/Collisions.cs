using System;
using System.Collections.Generic;
using System.Linq;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using Sandbox.Definitions;
using Sandbox.ModAPI;

namespace LLE
{
	public class Collisions
	{
		internal static Dictionary<MyDefinitionId, CollisionGeometry> _collisionGeometry;
		internal static Dictionary<MyDefinitionId, Traversability> _traversabilityCache;

		// Reusable buffer for DDA cell traversal. The mod is single-threaded.
		static readonly List<Vector3I> _gridLineCells = new List<Vector3I>();

		const float ProbeRadius = Constants.CollisionProbeRadius;

		public static void Load(IMyModContext ModContext)
		{
			const string collisions_bin = "Data/collisions.bin";
			if (!MyAPIGateway.Utilities.FileExistsInModLocation(collisions_bin, ModContext.ModItem))
			{
				MyConsole.Add($"ERROR: {collisions_bin} not found in mod location", Color.Red);
				return;
			}

			using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(collisions_bin, ModContext.ModItem))
			{
				var data = reader.ReadBytes((int)reader.BaseStream.Length);
				var textDict = MyAPIGateway.Utilities.SerializeFromBinary<Dictionary<DefinitionIdAsText, CollisionGeometry>>(data);
				_collisionGeometry = new Dictionary<MyDefinitionId, CollisionGeometry>(MyDefinitionId.Comparer);
				_traversabilityCache = new Dictionary<MyDefinitionId, Traversability>(MyDefinitionId.Comparer);
				foreach (var kv in textDict)
				{
					MyObjectBuilderType typeId;
					if (!MyObjectBuilderType.TryParse(kv.Key.TypeId, out typeId))
					{
						LLE.Log($"Error: Failed to parse TypeId: {kv.Key.TypeId}");
						continue;
					}
					var subtypeId = MyStringHash.GetOrCompute(kv.Key.SubtypeId);

					PreprocessCG(kv.Value);

					var defId = new MyDefinitionId(typeId, subtypeId);
					_collisionGeometry[defId] = kv.Value;
					_traversabilityCache[defId] = CalculateTraversability(kv.Value);
				}
			}
			MyConsole.Add($"Loaded {_collisionGeometry.Count} block collisions", Color.White);
		}

		// Returns a matrix that transforms from model space to world space.
		internal static MatrixD GetModelToWorldMatrix(IMySlimBlock block)
		{
			Matrix orientMatrix;
			block.Orientation.GetMatrix(out orientMatrix);

			Vector3D worldCenter;
			block.ComputeWorldCenter(out worldCenter);

			MatrixD modelToWorld = new MatrixD(orientMatrix) * block.CubeGrid.WorldMatrix;
			modelToWorld.Translation = worldCenter;
			return modelToWorld;
		}

		// Transform a point from world space to model space.
		internal static Vector3D WorldToModel(IMySlimBlock block, Vector3D worldPoint)
		{
			var modelToWorld = GetModelToWorldMatrix(block);
			MatrixD invModelToWorld;
			MatrixD.Invert(ref modelToWorld, out invModelToWorld);
			return Vector3D.Transform(worldPoint, invModelToWorld);
		}

		// Transform a point from model space to world space.
		private static Vector3D ModelToWorld(IMySlimBlock block, Vector3D modelPoint)
		{
			return Vector3D.Transform(modelPoint, GetModelToWorldMatrix(block));
		}

		public static void Draw(IMySlimBlock block)
		{
			IMyCubeGrid grid = block.CubeGrid;
			CollisionGeometry geometry;
			var id = block.BlockDefinition.Id;

			if (_collisionGeometry.TryGetValue(id, out geometry))
				Draw(geometry, GetModelToWorldMatrix(block));
		}

		private static void DrawConvexOutline(List<Vector3> modelVerts, Matrix shapeTransform,
									  MatrixD modelToWorld, float epsilon, Vector4 color)
		{
			var worldVerts = modelVerts.Select(v => 
				Vector3D.Transform(new Vector3D(Vector3.Transform(v, shapeTransform)), modelToWorld)).ToList();
			var screenVerts = Drawing.WorldToScreen(worldVerts);
			var hull = Geometry.ConvexHull(screenVerts);
			Drawing.Contour(hull.ToArray(), true, epsilon, color);
		}

		private static void Draw(CollisionGeometry geometry, MatrixD modelToWorld)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null)
					DrawConvexOutline(convex.Vertices, shape.Transform, modelToWorld, 1e-4f, new Vector4(1f, 0f, 0f, 1f));

				var sphere = shape as SphereShape;
				if (sphere != null)
				{
					var worldCenter = Vector3D.Transform(new Vector3D(sphere.Center), modelToWorld);
					Drawing.ScreenSphere(worldCenter, sphere.Radius, new Vector4(1f, 1f, 1f, 1f));
					Drawing.RoundMarker(worldCenter, Color.BlueViolet);
				}

				var capsule = shape as CapsuleShape;
				if (capsule != null)
				{
					var worldA = Vector3D.Transform(new Vector3D(Vector3.Transform(capsule.VertexA, shape.Transform)), modelToWorld);
					var worldB = Vector3D.Transform(new Vector3D(Vector3.Transform(capsule.VertexB, shape.Transform)), modelToWorld);
					Drawing.ScreenSphere(worldA, capsule.Radius, new Vector4(1f, 0f, 1f, 1f));
					Drawing.ScreenSphere(worldB, capsule.Radius, new Vector4(1f, 0f, 1f, 1f));
				}
			}
			foreach (var detector in geometry.Detectors)
			{
				var color = Color.Gray;
				if(detector.Name.StartsWith("terminal_")) color = Color.Cyan;
				else if(detector.Name.StartsWith("door_")) color = Color.OrangeRed;
				else if(detector.Name.StartsWith("advanceddoor_")) color = Color.Orange;
				else if(detector.Name.StartsWith("inventory_")) color = Color.Magenta;
				else if(detector.Name.StartsWith("conveyor_")) color = Color.Yellow;
				// panel
				else if(detector.Name.StartsWith("cockpit_")) color = Color.BlueViolet;
				else if(detector.Name.StartsWith("block_")) color = Color.White;
				// wardrobe, textpanel, cryopod

				var vertices = new List<Vector3>();
				Geometry.BoxToConvex(new Vector3(0.5f, 0.5f, 0.5f), vertices);
				DrawConvexOutline(vertices, detector.Transform, modelToWorld, 5e-5f, color.ToVector4());
			}
		}

		// Bake shape transforms into vertex/center positions.
		// After this, all geometry is in model space.
		private static void PreprocessCG(CollisionGeometry geometry)
		{
			var shapes = geometry.Shapes;

			for (int i = 0; i < shapes.Count; ++i)
			{
				var shape = shapes[i];

				var box = shape as BoxShape;
				if (box != null)
				{
					var vertices = new List<Vector3>();
					Geometry.BoxToConvex(box.HalfExtents, vertices);
					for (int v = 0; v < vertices.Count; ++v)
						vertices[v] = Vector3.Transform(vertices[v], box.Transform);
					shapes[i] = new ConvexHullShape { Vertices = vertices };
					continue;
				}

				var cylinder = shape as CylinderShape;
				if (cylinder != null)
				{
					var vertices = new List<Vector3>();
					Geometry.CylinderToConvex(cylinder.VertexA, cylinder.VertexB, cylinder.Radius, vertices);
					for (int v = 0; v < vertices.Count; ++v)
						vertices[v] = Vector3.Transform(vertices[v], cylinder.Transform);
					shapes[i] = new ConvexHullShape { Vertices = vertices };
					continue;
				}

				var convex = shape as ConvexHullShape;
				if (convex != null)
				{
					for (int v = 0; v < convex.Vertices.Count; ++v)
						convex.Vertices[v] = Vector3.Transform(convex.Vertices[v], convex.Transform);
					continue;
				}

				var capsule = shape as CapsuleShape;
				if (capsule != null)
				{
					Vector3 mA = Vector3.Transform(capsule.VertexA, capsule.Transform);
					Vector3 mB = Vector3.Transform(capsule.VertexB, capsule.Transform);

					var vertices = new List<Vector3>();
					Geometry.CapsuleToConvex(mA, mB, capsule.Radius, vertices);
					shapes[i] = new ConvexHullShape { Vertices = vertices };
					continue;
				}
			}

			for (int i = 0; i < geometry.Detectors.Count; ++i)
			{
				var d = geometry.Detectors[i];
				d.ForRaycast = BoxToPgList(Vector3.Half, d.Transform);
			}
		}

		private static List<Parallelogram> BoxToPgList(Vector3 halfExtents, Matrix transform)
		{
			var pList = new List<Parallelogram>();
			Geometry.BoxToParallelograms(halfExtents, pList);
			for (int v = 0; v < pList.Count; ++v)
			{
				Parallelogram p = pList[v];
				p.A = Vector3.Transform(p.A, transform);
				p.B = Vector3.Transform(p.B, transform);
				p.C = Vector3.Transform(p.C, transform);
				pList[v] = p;
			}

			return pList;
		}

		private static Traversability CalculateTraversability(CollisionGeometry geometry)
		{
			float blockSize = MyDefinitionManager.Static.GetCubeSize(MyCubeSize.Large);
			float offset = blockSize / 2;

			var trav = new Traversability();

			// Center probe
			if (ProbeIntersects(geometry, Vector3.Zero, ProbeRadius))
				trav[0, 0, 0] = true;

			// 6 directional probes around the center
			var dirs = Constants.SixDirections;
			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				Vector3 probeCenter = new Vector3(dir.X, dir.Y, dir.Z) * offset;
				if (ProbeIntersects(geometry, probeCenter, ProbeRadius))
					trav[dir] = true;
			}

			return trav;
		}

		internal static bool ProbeIntersects(CollisionGeometry geometry, Vector3D center, double radius)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.SphereVsConvex(center, radius, convex.Vertices))
					return true;
				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.SphereVsSphere(
					center, radius, new Vector3D(sphere.Center), sphere.Radius))
					return true;
			}
			return false;
		}

		internal static bool ProbeIntersects(CollisionGeometry geometry, Vector3 center, double radius)
		{
			return ProbeIntersects(geometry, new Vector3D(center), radius);
		}

		internal static bool LineIntersects(CollisionGeometry geometry, Vector3 A, Vector3 B)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.LineSegmentVsConvex(A, B, convex.Vertices))
					return true;
				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.LineSegmentVsSphere(A, B, sphere.Center, sphere.Radius))
					return true;
			}
			return false;
		}

		internal static bool LineIntersectsGridGeometry(IMyCubeGrid grid, LineD worldLine, Vector3I min, Vector3I max)
		{
			// GridIntersection.Calculate works in grid-local space (meters from grid origin)
			// and treats cell corners as cell centers, so convert the world-space line to
			// local and add half a cell (matching MyCubeGrid.RayCastCells).
			MatrixD invWorld = grid.PositionComp.WorldMatrixNormalizedInv;
			Vector3D halfOffset = new Vector3D(grid.GridSize * 0.5f);
			Vector3D localFrom = Vector3D.Transform(worldLine.From, invWorld) + halfOffset;
			Vector3D localTo   = Vector3D.Transform(worldLine.To,   invWorld) + halfOffset;

			_gridLineCells.Clear();
			GridIntersection.Calculate(_gridLineCells, grid.GridSize, localFrom, localTo, min, max);

			foreach (var cell in _gridLineCells)
			{
				var slim = grid.GetCubeBlock(cell);
				if (slim == null) continue;

				CollisionGeometry cellGeometry;
				if (!_collisionGeometry.TryGetValue(slim.BlockDefinition.Id, out cellGeometry))
					return true; // for unknown block

				var modelFrom = WorldToModel(slim, worldLine.From); // XX double inverse matrix calculatuion
				var modelTo = WorldToModel(slim, worldLine.To);
				if (LineIntersects(cellGeometry, modelFrom, modelTo))
					return true;
			}
			return false;
		}

		public static void DrawTraversability(IMyCubeGrid grid, Vector3I position)
		{
			var calc = new TraversabilityCalculator(grid, 0);
			Traversability t = calc.GetTraversability(position);
			var zero = grid.GridIntegerToWorld(position);

			// Draw probe spheres at the same positions used for traversability calculation
			float blockSize = MyDefinitionManager.Static.GetCubeSize(MyCubeSize.Large);

			float offset = blockSize/2;

			var color = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);

			Drawing.ScreenSphere(zero, ProbeRadius, color);
			var dirs = Constants.SixDirections;

			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				var world = zero + offset * Vector3D.TransformNormal(new Vector3D(dir.X, dir.Y, dir.Z), grid.WorldMatrix);
				Drawing.ScreenSphere(world, ProbeRadius, color);
				Drawing.RoundMarker(world, t[dirs[d]] ? Color.Black : Color.Lime);
			}
			Drawing.RoundMarker(zero, t[0, 0, 0] ? Color.Black : Color.Green);
		}

		public static bool CheckWorldSphere(IMySlimBlock block, Vector3D worldCenter, double radius)
		{
			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			Vector3D modelCenter = WorldToModel(block, worldCenter);
			return ProbeIntersects(geometry, modelCenter, radius);
		}

		// Return the world-space center of the nearest collision shape to the given point.
		// Returns null if the block has no collision geometry.
		public static bool GetNearestCollisionCenter(IMySlimBlock block, Vector3D worldPoint, out Vector3D result)
		{
			result = Vector3D.Zero;

			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			Vector3D modelPoint = WorldToModel(block, worldPoint);

			double bestDistSq = double.MaxValue;
			Vector3? bestModelCenter = null;

			foreach (var shape in geometry.Shapes)
			{
				Vector3 center = GetShapeModelCenter(shape);
				Vector3D diff = new Vector3D(center) - modelPoint;
				double distSq = diff.LengthSquared();
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestModelCenter = center;
				}
			}

			if (bestModelCenter == null) return false;
			result = ModelToWorld(block, new Vector3D(bestModelCenter.Value));
			return true;
		}

		public static bool GetNearestDetectorCenterByPrefix(IMySlimBlock block, Vector3D worldPoint, string namePrefix, out Vector3D result)
		{
			result = Vector3D.Zero;
			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			Vector3D modelPoint = WorldToModel(block, worldPoint);

			double bestDistSq = double.MaxValue;
			Vector3? bestModelCenter = null;

			foreach (var detector in geometry.Detectors)
			{
				if (!detector.Name.StartsWith(namePrefix)) continue;

				// Detector is a unit cube centered at origin with half-extents (0.5, 0.5, 0.5).
				Vector3 center = detector.Transform.Translation;
				Vector3D diff = new Vector3D(center) - modelPoint;
				double distSq = diff.LengthSquared();
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestModelCenter = center;
				}
			}

			if (bestModelCenter == null) return false;
			result = ModelToWorld(block, new Vector3D(bestModelCenter.Value));
			return true;
		}

		// Returns the best available world-space target point for a block:
		// nearest collision center, or model AABB center, or cell center.
		public static Vector3D GetGrindWeldTarget(IMySlimBlock block, Vector3D worldPoint)
		{
			Vector3D result;
			if (GetNearestCollisionCenter(block, worldPoint, out result))
				return result;

			var fat = block.FatBlock;
			if (fat != null)
				return fat.PositionComp.WorldAABB.Center;

			block.ComputeWorldCenter(out result);
			return result;
		}

		private static Vector3 GetShapeModelCenter(CollisionShape shape)
		{
			var convex = shape as ConvexHullShape;
			if (convex != null)
				return convex.Vertices.Aggregate(Vector3.Zero, (a, v) => a + v) / convex.Vertices.Count;

			var sphere = shape as SphereShape;
			if (sphere != null)
				return sphere.Center;

			var capsule = shape as CapsuleShape;
			if (capsule != null)
				return 0.5f * (Vector3.Transform(capsule.VertexA, shape.Transform) + Vector3.Transform(capsule.VertexB, shape.Transform));

			return Vector3.Zero;
		}

		public static Traversability GetBlockTraversability(IMySlimBlock slim, Vector3I position)
		{
			Traversability t;
			if (!_traversabilityCache.TryGetValue(slim.BlockDefinition.Id, out t))
				return Traversability.Blocked;

			if (slim.Min == slim.Max)
				return Traversability.Rotate(t, new MatrixI(slim.Orientation));

			return CalculateMultiBlockTraversability(slim, position);
		}

		public static bool CenterIsFree(IMySlimBlock slim, Vector3I position)
		{
			if (slim == null) return true;
			return !GetBlockTraversability(slim, position).Center;
		}

		public static Traversability CalculateMultiBlockTraversability(IMySlimBlock slim, Vector3I position)
		{
			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(slim.BlockDefinition.Id, out geometry))
				return Traversability.Blocked;

			float blockSize = MyDefinitionManager.Static.GetCubeSize(MyCubeSize.Large);
			float offset = blockSize / 2;

			// Block center in grid-integer space (each unit = one cell)
			Vector3 blockCenterGrid = (slim.Min + slim.Max + Vector3I.One) * 0.5f;
			// Sub-cell center in grid-integer space
			Vector3 cellCenterGrid = position + Vector3.One * 0.5f;
			// Transform probe positions from grid-aligned to model space (canonical orientation)
			Matrix orient;
			slim.Orientation.GetMatrix(out orient);
			Matrix invOrient = Matrix.Transpose(orient);
			Vector3 modelCellCenter = Vector3.Transform((cellCenterGrid - blockCenterGrid) * blockSize, invOrient);

			var trav = new Traversability();

			// Center probe
			if (ProbeIntersects(geometry, modelCellCenter, ProbeRadius))
				trav[0, 0, 0] = true;

			// 6 directional probes around the cell
			var dirs = Constants.SixDirections;
			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				Vector3 modelProbe = modelCellCenter + Vector3.Transform(new Vector3(dir.X, dir.Y, dir.Z), invOrient) * offset;
				if (ProbeIntersects(geometry, modelProbe, ProbeRadius))
					trav[dir] = true;
			}

			return trav;
		}

		public static void GetInteractionPoints(IMySlimBlock block, List<Vector3I> inventoryIP, List<Vector3I> medblockIP)
		{
			Debug.linesRed.Clear();
			Debug.linesGray.Clear();

			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return;

			List<float> inventoryDistance = new List<float>();
			List<float> medblockDistance = new List<float>();

			var grid = block.CubeGrid;

			var min = block.Min-1;
			var max = block.Max+1;

			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var ijkBlock = grid.GetCubeBlock(ijk);
				if(ijkBlock != null && !CenterIsFree(ijkBlock, ijk)) continue;

				Vector3D worldFrom = grid.GridIntegerToWorld(ijk);
				Vector3 modelFrom = WorldToModel(block, worldFrom);

				foreach (var detector in geometry.Detectors)
				{	
					bool inventory =
						detector.Name.StartsWith("conveyor_") ||
						detector.Name.StartsWith("inventory_") || 
						detector.Name.StartsWith("cockpit_");
					bool medblock = detector.Name.StartsWith("block_");

					if(!inventory && !medblock) continue;

					var detectorCenter = detector.Transform.Translation;
					var line = new Line(modelFrom, detectorCenter);
					
					if(line.Length > Constants.MaxInteractionDistance) continue;

					float minIntersection = float.MaxValue;

					foreach(var p in detector.ForRaycast)
					{	var lp = p;
						var f = Intersections.GetLineParallelogramIntersection(ref line, ref lp);
						if(!f.HasValue) continue;

						if(f.Value < minIntersection) minIntersection = f.Value;
					}

					if(minIntersection >= float.MaxValue) continue;

					var clippedByDetector = new Line(line.From, line.From + line.Direction * minIntersection);
					var worldLine = new LineD(worldFrom, ModelToWorld(block, clippedByDetector.To));

					if(LineIntersectsGridGeometry(grid, worldLine, min, max))
					{	Drawing.RoundMarker(worldLine.To, Color.Gray);
						continue;
					}

					Drawing.RoundMarker(worldLine.To, Color.Green);

					Debug.linesRed.Add(worldLine);

					if(inventory)
					{	inventoryIP.Add(ijk);
						inventoryDistance.Add(minIntersection);
					}
					if(medblock)
					{	medblockIP.Add(ijk);
						medblockDistance.Add(minIntersection);
					}
				}
			}

			SelectNearest(inventoryIP, inventoryDistance);
			SelectNearest(medblockIP, medblockDistance);
		}

		private static void SelectNearest(List<Vector3I> ijk, List<float> distance)
		{
			if (distance.Count == 0) return;

			float min = distance[0];
			for (int n = 1; n < distance.Count; ++n)
				if (distance[n] < min) min = distance[n];

			float threshold = min + 0.25f;

			for (int n = distance.Count - 1; n >= 0; --n)
			{	if (distance[n] <= threshold) continue;

				ijk.RemoveAt(n);
				distance.RemoveAt(n);
			}
		}
	}
}
