using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.ObjectBuilders;
using VRageMath;

using System.Linq;

namespace LLE
{
	public class Collisions
	{
		internal static Dictionary<MyDefinitionId, CollisionGeometry> _collisionGeometry;

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

					_collisionGeometry[new MyDefinitionId(typeId, subtypeId)] = kv.Value;
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
					var axis = Vector3.Normalize(cylinder.VertexB - cylinder.VertexA);
					Vector3 right, localUp;
					Geometry.OrthonormalBasis(axis, out right, out localUp);
					var vv = new List<Vector3>();
					int segments = 32;
					for (int s = 0; s < segments; s++)
					{
						double angle = s * MathHelper.TwoPi / segments;
						double c = Math.Cos(angle), sn = Math.Sin(angle);
						Vector3 offset = (float)c * right * cylinder.Radius + (float)sn * localUp * cylinder.Radius;
						vv.Add(cylinder.VertexA + offset);
						vv.Add(cylinder.VertexB + offset);
					}
					for (int v = 0; v < vv.Count; ++v)
						vv[v] = Vector3.Transform(vv[v], cylinder.Transform);

					shapes[i] = new ConvexHullShape { Vertices = vv };
				}
			}
		}
	}
}
