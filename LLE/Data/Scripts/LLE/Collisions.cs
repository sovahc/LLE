using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.ObjectBuilders;
using VRageMath;

using System.Linq;
using Sandbox.Definitions;

namespace LLE
{
	/// <summary>
	/// Stores block traversability data as a 3x3x3 bit cube.
	/// Indexed from -1 to 1 along each axis.
	/// </summary>
	public struct Traversability
	{
		private uint _mask;

		private void Check(int dx, int dy, int dz)
		{	if (dx < -1 || dx > 1 || dy < -1 || dy > 1 || dz < -1 || dz > 1)
				throw new Exception($"Traversability index out of range: {dx}, {dy}, {dz}");		
		}

		// Bit mask index: (dx+1)*9 + (dy+1)*3 + (dz+1)
		// Range: 0..26
		public bool this[int dx, int dy, int dz]
		{
			get
			{	Check(dx, dy, dz);

				int bit = (dx + 1) * 9 + (dy + 1) * 3 + (dz + 1);
				return (_mask & (1u << bit)) != 0;
			}
			set
			{	Check(dx, dy, dz);

				int bit = (dx + 1) * 9 + (dy + 1) * 3 + (dz + 1);
				if (value)
					_mask |= (1u << bit);
				else
					_mask &= ~(1u << bit);
			}
		}

		/// <summary>
		/// Whether the engineer can stay/turn around in the center of the block.
		/// </summary>
		public bool CanStayInCenter => this[0, 0, 0];

		public void Clear() => _mask = 0;

		public void SetAll(bool value)
		{
			if (value)
				_mask = (1u << 27) - 1; // Set first 27 bits to 1
			else
				_mask = 0;
		}
	}

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
					var worldVerts = convex.Vertices.Select(v => Vector3D.Transform(new Vector3D(Vector3.Transform(v, shape.Transform)), blockMatrix)).ToList();
					var screenVerts = Drawing.WorldToScreen(worldVerts);
					var hull = Geometry.ConvexHull(screenVerts);
					if (hull.Count >= 2)
						Drawing.Contour(hull.ToArray(), true, 5e-5f, new Vector4(1f, 0f, 0f, 1f));
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


		private static Traversability CalculateTraversability(CollisionGeometry geometry)
		{
			float blockSize = MyDefinitionManager.Static.GetCubeSize(MyCubeSize.Large);

			var trav = new Traversability();
			var probe = new List<Vector3>();
			
			const float EngineerCapsuleHeight = 1.8f;
			const float EngineerCapsuleRadius = 1.0f; // Don't delete this

			var ech_d2 = EngineerCapsuleHeight/2;
			var he = new Vector3(ech_d2, ech_d2, ech_d2);
			Geometry.BoxToConvex(he, probe);

			float offset = blockSize - EngineerCapsuleHeight + 0.1f;

			if (!ProbeIntersectsCollision(probe, geometry, 0, 0, 0)) trav[0, 0, 0] = true;

			if (!ProbeIntersectsCollision(probe, geometry, +offset, 0, 0)) trav[1, 0, 0] = true;
			if (!ProbeIntersectsCollision(probe, geometry, -offset, 0, 0)) trav[-1, 0, 0] = true;
			if (!ProbeIntersectsCollision(probe, geometry, 0, +offset, 0)) trav[0, 1, 0] = true;
			if (!ProbeIntersectsCollision(probe, geometry, 0, -offset, 0)) trav[0, -1, 0] = true;
			if (!ProbeIntersectsCollision(probe, geometry, 0, 0, +offset)) trav[0, 0, 1] = true;
			if (!ProbeIntersectsCollision(probe, geometry, 0, 0, -offset)) trav[0, 0, -1] = true;

			return trav;
		}

		private static bool ProbeIntersectsCollision(List<Vector3> probeConvex, CollisionGeometry geometry, float ox, float oy, float oz)
		{
			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex == null) continue;

				var shiftedProbe = probeConvex.Select(v => new Vector3(v.X + ox, v.Y + oy, v.Z + oz)).ToList();
				if (Intersections.ConvexVsConvex(shiftedProbe, convex.Vertices))
					return true;
			}
			return false;
		}
	}
}
