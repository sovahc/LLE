using System.Collections.Generic;
using System.Linq;

using Sandbox.Definitions;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.ObjectBuilders;
using VRageMath;

namespace LLE
{
	public class Collisions
	{
		internal static Dictionary<MyDefinitionId, CollisionGeometry> _collisionGeometry;
		internal static Dictionary<MyDefinitionId, Traversability> _traversabilityCache;

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
						Utilities.Log($"Error: Failed to parse TypeId: {kv.Key.TypeId}");
						continue;
					}
					var subtypeId = MyStringHash.GetOrCompute(kv.Key.SubtypeId);

					// XX: default collision is cube.

					PreprocessCG(kv.Value);

					var defId = new MyDefinitionId(typeId, subtypeId);
					_collisionGeometry[defId] = kv.Value;
					_traversabilityCache[defId] = CalculateTraversability(kv.Value);
				}
			}
			MyConsole.Add($"Loaded {_collisionGeometry.Count} block collisions", Color.White);
		}

		public static void Draw(IMyCubeGrid grid_A, IMySlimBlock block)
		{
			CollisionGeometry geometry;

			var id = block.BlockDefinition.Id;

			if (_collisionGeometry.TryGetValue(id, out geometry))
			{
				Matrix bo;
				block.Orientation.GetMatrix(out bo);
				Quaternion q = Quaternion.CreateFromRotationMatrix(grid_A.WorldMatrix);

				Matrix.Transform(ref bo, ref q, out bo);

				var blockCenter = 0.5 * (grid_A.GridIntegerToWorld(block.Min) + grid_A.GridIntegerToWorld(block.Max));
				MatrixD blockMatrix = new MatrixD(bo)
				{
					Translation = blockCenter
				};

				Draw(geometry, blockMatrix);
			}
		}

		private static void Draw(CollisionGeometry geometry, MatrixD blockMatrix)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null)
				{
					var worldVerts = convex.Vertices.Select(v =>
					{
						var localVert = Vector3.Transform(v, shape.Transform);
						return Vector3D.Transform(new Vector3D(localVert), blockMatrix);
					}).ToList();
					var screenVerts = Drawing.WorldToScreen(worldVerts);
					var hull = Geometry.ConvexHull(screenVerts);
					Drawing.Contour(hull.ToArray(), true, 1e-4f, new Vector4(1f, 0f, 0f, 1f));
				}

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
				for (int v = 0; v < vertices.Count; ++v)
					vertices[v] = Vector3.Transform(vertices[v], detector.Transform);

				var worldVerts = vertices.Select(v => Vector3D.Transform(new Vector3D(v), blockMatrix)).ToList();
				var screenVerts = Drawing.WorldToScreen(worldVerts);
				var hull = Geometry.ConvexHull(screenVerts);
				Drawing.Contour(hull.ToArray(), true, 5e-5f, color.ToVector4());
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
				}
			}
		}

		private const float probeRadius = 0.6f;

		private static Traversability CalculateTraversability(CollisionGeometry geometry)
		{
			float blockSize = MyDefinitionManager.Static.GetCubeSize(MyCubeSize.Large);

			var trav = new Traversability();

			float offset = blockSize/2;

			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, 0, 0, 0)) trav[0, 0, 0] = true;

			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, +offset, 0, 0)) trav[+1, 0, 0] = true;
			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, -offset, 0, 0)) trav[-1, 0, 0] = true;
			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, 0, +offset, 0)) trav[0, +1, 0] = true;
			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, 0, -offset, 0)) trav[0, -1, 0] = true;
			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, 0, 0, +offset)) trav[0, 0, +1] = true;
			if (ProbeIntersectsCollision(Vector3.Zero, probeRadius, geometry, 0, 0, -offset)) trav[0, 0, -1] = true;

			return trav;
		}

		private static bool ProbeIntersectsCollision(Vector3 center, double radius, CollisionGeometry geometry, float ox, float oy, float oz)
		{
			var shiftedCenter = new Vector3D(center.X + ox, center.Y + oy, center.Z + oz);

			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null)
					if (Intersections.SphereVsConvex(shiftedCenter, radius, convex.Vertices))
						return true;
				var sphere = shape as SphereShape;
				if (sphere != null)
					if (Intersections.SphereVsSphere(shiftedCenter, radius,
						new Vector3D(sphere.Transform.Translation), sphere.Radius))
						return true;
			}
			return false;
		}

		public static void DrawTraversability(IMyCubeGrid grid, Vector3I position)
		{
			var calc = new TraversabilityCalculator(grid, 0);
			Traversability t = calc.GetAtGridPosition(position);
			var zero = grid.GridIntegerToWorld(position);

			// Draw probe spheres at the same positions used for traversability calculation
			float blockSize = MyDefinitionManager.Static.GetCubeSize(MyCubeSize.Large);

			float offset = blockSize/2;

			var color = new Vector4(0.25f, 0.25f, 0.25f, 1.0f);

			Drawing.ScreenSphere(zero, probeRadius, color);
			var dirs = Constants.SixDirections;

			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				var world = zero + offset * Vector3D.TransformNormal(new Vector3D(dir.X, dir.Y, dir.Z), grid.WorldMatrix);
				Drawing.ScreenSphere(world, probeRadius, color);
				Drawing.RoundMarker(world, t[dirs[d]] ? Color.Black : Color.Lime);
			}
			Drawing.RoundMarker(zero, t[0, 0, 0] ? Color.Black : Color.Green);
		}

		public static bool CheckWorldSphere(IMyCubeGrid grid, IMySlimBlock block, Vector3D worldCenter, double radius)
		{
			CollisionGeometry geometry;
			if (!_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry)) return false;

			Matrix bo;
			block.Orientation.GetMatrix(out bo);
			Quaternion q = Quaternion.CreateFromRotationMatrix(grid.WorldMatrix);
			Matrix.Transform(ref bo, ref q, out bo);

			var blockCenter = 0.5 * (grid.GridIntegerToWorld(block.Min) + grid.GridIntegerToWorld(block.Max));
			MatrixD blockMatrix = new MatrixD(bo) { Translation = blockCenter };

			MatrixD invBlock;
			MatrixD.Invert(ref blockMatrix, out invBlock);
			Vector3D localCenter = Vector3D.Transform(worldCenter, invBlock);

			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.SphereVsConvex(localCenter, radius, convex.Vertices))
					return true;

				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.SphereVsSphere(localCenter, radius,
					new Vector3D(sphere.Transform.Translation), sphere.Radius))
					return true;
			}
			return false;
		}

		public static bool CenterIsFree(IMySlimBlock slim, Vector3I position)
		{
			if(slim == null) return true;

			Traversability t;
			if (!_traversabilityCache.TryGetValue(slim.BlockDefinition.Id, out t))
				return false;

			Traversability result;
			if (slim.Min == slim.Max)
			{
				MatrixI m = new MatrixI(slim.Orientation);
				result = Traversability.Rotate(t, m);
			}
			else
			{
				result = CalculateMultiBlockTraversability(slim, position);
			}

			return result.Center == false;
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
			if (ProbeIntersectsLocal(localCellCenter, probeRadius, geometry))
				trav[0, 0, 0] = true;

			// 6 directional probes around the cell
			var dirs = Constants.SixDirections;
			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				Vector3 localProbe = localCellCenter + Vector3.Transform(new Vector3(dir.X, dir.Y, dir.Z), invOrient) * offset;
				if (ProbeIntersectsLocal(localProbe, probeRadius, geometry))
					trav[dir] = true;
			}

			return trav;
		}

		private static bool ProbeIntersectsLocal(Vector3 center, double radius, CollisionGeometry geometry)
		{
			var c = new Vector3D(center);
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null && Intersections.SphereVsConvex(c, radius, convex.Vertices))
					return true;

				var sphere = shape as SphereShape;
				if (sphere != null && Intersections.SphereVsSphere(
					c, radius, new Vector3D(sphere.Transform.Translation), sphere.Radius))
					return true;
			}
			return false;
		}
	}
}
