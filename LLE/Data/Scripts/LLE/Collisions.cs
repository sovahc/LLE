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
	public partial class Collisions
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

		internal static bool LineIntersectsGridGeometry(IMyCubeGrid grid, LineD worldLine, Vector3I min, Vector3I max,
			List<IMySlimBlock> outIntersected)
		{
			bool returnOnFirstFound = outIntersected == null;

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

				if(outIntersected != null &&
					outIntersected.Contains(slim)) continue; // Do not test twice

				CollisionGeometry cellGeometry;
				if (!_collisionGeometry.TryGetValue(slim.BlockDefinition.Id, out cellGeometry))
				{	if(returnOnFirstFound) return true; // For unknown block

					outIntersected.Add(slim);
					continue;
				}

				var modelFrom = Transform.WorldToModel(slim, worldLine.From); // XX double inverse matrix calculatuion
				var modelTo = Transform.WorldToModel(slim, worldLine.To);
				if (LineIntersects(cellGeometry, modelFrom, modelTo))
				{	
					if(returnOnFirstFound) return true;

					outIntersected.Add(slim);
				}
			}

			return outIntersected != null && outIntersected.Count != 0;
		}

		internal static bool ConvexVsBlockGeometry(CollisionGeometry geometry, List<Vector3> modelVerts)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.ConvexVsConvex(modelVerts, convex.Vertices))
					return true;
				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.SphereVsConvex(new Vector3D(sphere.Center), sphere.Radius, modelVerts))
					return true;
			}
			return false;
		}

		internal static bool ConvexVsGridGeometry(IMyCubeGrid grid, List<Vector3D> worldSpace, Vector3I min, Vector3I max,
			List<IMySlimBlock> outIntersected)
		{
			bool returnOnFirstFound = outIntersected == null;

			var modelSpace = new List<Vector3>(worldSpace.Count);

			var iterator = new Vector3I_RangeIterator(ref min, ref max);
			for (; iterator.IsValid(); iterator.MoveNext())
			{
				var slim = grid.GetCubeBlock(iterator.Current);
				if (slim == null) continue;

				if (outIntersected != null && outIntersected.Contains(slim)) continue;

				CollisionGeometry geometry;
				if (!_collisionGeometry.TryGetValue(slim.BlockDefinition.Id, out geometry))
				{	// treat the unknown block as solid
					
					if(returnOnFirstFound) return true;

					outIntersected.Add(slim);
					continue;
				}

				var w2m = Transform.GetWorldToModelMatrix(slim);

				modelSpace.Clear();
				for (int i = 0; i < worldSpace.Count; i++)
					modelSpace.Add(Vector3D.Transform(worldSpace[i], w2m));

				if (ConvexVsBlockGeometry(geometry, modelSpace))
				{
					if(returnOnFirstFound) return true;

					outIntersected.Add(slim);
				}
			}

			return outIntersected != null && outIntersected.Count != 0;
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

			Vector3D modelCenter = Transform.WorldToModel(block, worldCenter);
			return ProbeIntersects(geometry, modelCenter, radius);
		}

		// Return the world-space center of the nearest collision shape to the given point.
		// Returns null if the block has no collision geometry.
		public static bool GetNearestCollisionCenter(IMySlimBlock block, Vector3D worldPoint, out Vector3D result)
		{
			result = Vector3D.Zero;

			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			Vector3D modelPoint = Transform.WorldToModel(block, worldPoint);

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
			result = Transform.ModelToWorld(block, new Vector3D(bestModelCenter.Value));
			return true;
		}

		public static bool GetNearestDetectorCenterByPrefix(IMySlimBlock block, Vector3D worldPoint, string namePrefix, out Vector3D result)
		{
			result = Vector3D.Zero;
			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			Vector3D modelPoint = Transform.WorldToModel(block, worldPoint);

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
			result = Transform.ModelToWorld(block, new Vector3D(bestModelCenter.Value));
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
	}
}
