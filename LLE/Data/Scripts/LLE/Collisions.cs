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

		private static MatrixD GetBlockWorldMatrix(IMySlimBlock block)
		{
			Matrix localMatrix;
			block.Orientation.GetMatrix(out localMatrix);

			Vector3D worldCenter;
			block.ComputeWorldCenter(out worldCenter);

			MatrixD worldMatrix = new MatrixD(localMatrix) * block.CubeGrid.WorldMatrix;
			worldMatrix.Translation = worldCenter;
			return worldMatrix;
		}

		public static void Draw(IMySlimBlock block)
		{
			IMyCubeGrid grid = block.CubeGrid;
			CollisionGeometry geometry;
			var id = block.BlockDefinition.Id;

			if (_collisionGeometry.TryGetValue(id, out geometry))
				Draw(geometry, GetBlockWorldMatrix(block));
		}

		private static void DrawConvexOutline(List<Vector3> localVerts, Matrix localTransform,
											  MatrixD blockMatrix, float epsilon, Vector4 color)
		{
			var worldVerts = localVerts.Select(v => 
				Vector3D.Transform(new Vector3D(Vector3.Transform(v, localTransform)), blockMatrix)).ToList();
			var screenVerts = Drawing.WorldToScreen(worldVerts);
			var hull = Geometry.ConvexHull(screenVerts);
			Drawing.Contour(hull.ToArray(), true, epsilon, color);
		}

		private static void Draw(CollisionGeometry geometry, MatrixD blockMatrix)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null)
					DrawConvexOutline(convex.Vertices, shape.Transform, blockMatrix, 1e-4f, new Vector4(1f, 0f, 0f, 1f));

				var sphere = shape as SphereShape;
				if (sphere != null)
				{
					var worldCenter = Vector3D.Transform(new Vector3D(shape.Transform.Translation), blockMatrix);
					Drawing.ScreenSphere(worldCenter, sphere.Radius, new Vector4(1f, 1f, 1f, 1f));
					Drawing.RoundMarker(worldCenter, Color.BlueViolet);
				}

				var capsule = shape as CapsuleShape;
				if (capsule != null)
				{
					var worldA = Vector3D.Transform(new Vector3D(Vector3.Transform(capsule.VertexA, shape.Transform)), blockMatrix);
					var worldB = Vector3D.Transform(new Vector3D(Vector3.Transform(capsule.VertexB, shape.Transform)), blockMatrix);
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
				DrawConvexOutline(vertices, detector.Transform, blockMatrix, 5e-5f, color.ToVector4());
			}
		}

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
					var c = cylinder;
					Geometry.CylinderToConvex(c.VertexA, c.VertexB, c.Radius, vertices);
					for (int v = 0; v < vertices.Count; ++v)
						vertices[v] = Vector3.Transform(vertices[v], cylinder.Transform);

					shapes[i] = new ConvexHullShape { Vertices = vertices };
					continue;
				}
				// XXX Capsule
			}
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

		private static bool ProbeIntersects(CollisionGeometry geometry, Vector3D center, double radius)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.SphereVsConvex(center, radius, convex.Vertices))
					return true;
				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.SphereVsSphere(
					center, radius, new Vector3D(sphere.Transform.Translation), sphere.Radius))
					return true;
			}
			return false;
		}

		private static bool ProbeIntersects(CollisionGeometry geometry, Vector3 center, double radius)
		{
			return ProbeIntersects(geometry, new Vector3D(center), radius);
		}

		private static bool LineIntersects(CollisionGeometry geometry, Vector3D start, Vector3D end)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.LineSegmentVsConvex(start, end, convex.Vertices))
					return true;
				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.LineSegmentVsSphere(
					start, end, new Vector3D(sphere.Transform.Translation), sphere.Radius))
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

			var blockMatrix = GetBlockWorldMatrix(block);

			MatrixD invBlock;
			MatrixD.Invert(ref blockMatrix, out invBlock);
			Vector3D localCenter = Vector3D.Transform(worldCenter, invBlock);

			return ProbeIntersects(geometry, localCenter, radius);
		}

		// Return the world-space center of the nearest collision shape to the given point.
		// Returns null if the block has no collision geometry.
		public static bool GetNearestCollisionCenter(IMySlimBlock block, Vector3D worldPoint, out Vector3D result)
		{
			result = Vector3D.Zero;

			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			var blockMatrix = GetBlockWorldMatrix(block);
			MatrixD invBlock;
			MatrixD.Invert(ref blockMatrix, out invBlock);
			Vector3D localPoint = Vector3D.Transform(worldPoint, invBlock);

			double bestDistSq = double.MaxValue;
			Vector3? bestLocalCenter = null;

			foreach (var shape in geometry.Shapes)
			{
				Vector3 center = GetShapeLocalCenter(shape);
				Vector3D diff = new Vector3D(center) - localPoint;
				double distSq = diff.LengthSquared();
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestLocalCenter = center;
				}
			}

			if (bestLocalCenter == null) return false;
			result = Vector3D.Transform(new Vector3D(bestLocalCenter.Value), blockMatrix);
			return true;
		}

		public static void GetInteractionPoints(IMySlimBlock block, List<Vector3I> output)
		{
			string prefix1 = "conveyor_";
			string prefix2 = "block_"; // recharge point

			var grid = block.CubeGrid;

			var min = block.Min-1;
			var max = block.Max+1;

			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{
				var ijk = iter.Current;

				var b = grid.GetCubeBlock(ijk);
				if(b != null && !CenterIsFree(b, ijk)) continue;

				Vector3D world = grid.GridIntegerToWorld(ijk);
				Vector3D ipWorld;
				if(//!GetNearestDetectorCenterByPrefix(block, world, prefix1, out ipWorld) &&
					!GetNearestDetectorCenterByPrefix(block, world, prefix2, out ipWorld)) continue;

				if((world - ipWorld).Length() > 2.75) continue;

				output.Add(ijk);
			}
		}

		// Return the world-space center of the nearest detector whose name starts with `namePrefix`.
		// Returns null if no matching detector is found or the block has no collision geometry.
		public static bool GetNearestDetectorCenterByPrefix(IMySlimBlock block, Vector3D worldPoint, string namePrefix, out Vector3D result)
		{
			result = Vector3D.Zero;
			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			var blockMatrix = GetBlockWorldMatrix(block);
			MatrixD invBlock;
			MatrixD.Invert(ref blockMatrix, out invBlock);
			Vector3D localPoint = Vector3D.Transform(worldPoint, invBlock);

			double bestDistSq = double.MaxValue;
			Vector3? bestLocalCenter = null;

			foreach (var detector in geometry.Detectors)
			{
				if (!detector.Name.StartsWith(namePrefix)) continue;

				// Detector is a unit cube centered at origin with half-extents (0.5, 0.5, 0.5).
				Vector3 center = detector.Transform.Translation;
				Vector3D diff = new Vector3D(center) - localPoint;
				double distSq = diff.LengthSquared();
				if (distSq < bestDistSq)
				{
					bestDistSq = distSq;
					bestLocalCenter = center;
				}
			}

			if (bestLocalCenter == null) return false;
			result = Vector3D.Transform(new Vector3D(bestLocalCenter.Value), blockMatrix);
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

		private static Vector3 GetShapeLocalCenter(CollisionShape shape)
		{
			var convex = shape as ConvexHullShape;
			if (convex != null)
				return convex.Vertices.Aggregate(Vector3.Zero, (a, v) => a + v) / convex.Vertices.Count;

			var sphere = shape as SphereShape;
			if (sphere != null)
				return shape.Transform.Translation;

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
			Vector3 localCellCenter = Vector3.Transform((cellCenterGrid - blockCenterGrid) * blockSize, invOrient);

			var trav = new Traversability();

			// Center probe
			if (ProbeIntersects(geometry, localCellCenter, ProbeRadius))
				trav[0, 0, 0] = true;

			// 6 directional probes around the cell
			var dirs = Constants.SixDirections;
			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				Vector3 localProbe = localCellCenter + Vector3.Transform(new Vector3(dir.X, dir.Y, dir.Z), invOrient) * offset;
				if (ProbeIntersects(geometry, localProbe, ProbeRadius))
					trav[dir] = true;
			}

			return trav;
		}
	}
}
