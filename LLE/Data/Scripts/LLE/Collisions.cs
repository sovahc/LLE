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
		private static Dictionary<MyDefinitionId, CollisionGeometry> _collisionGeometry;

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
				MatrixD blockMatrix = new MatrixD(bo)
				{
					Translation = grid_A.GridIntegerToWorld(block.Position)
				};

				Draw(geometry, blockMatrix);
			}
		}

		private static void Draw(CollisionGeometry geometry, MatrixD blockMatrix)
		{
			var material = MyStringId.GetOrCompute("Square");
			foreach (var shape in geometry.Shapes)
			{
				var matrix = blockMatrix * shape.Transform;

				var convex = shape as ConvexHullShape;
				if (convex != null)
				{
					var worldVerts = convex.Vertices.Select(v => Vector3D.Transform(v, matrix)).ToList();
					var screenVerts = Drawing.WorldToScreen(worldVerts);
					var hull = Geometry.ConvexHull(screenVerts);
					if (hull.Count >= 2)
					{
						var hullArray = hull.ToArray();
						Drawing.Contour(hullArray, true, 5e-5f, new Vector4(1f, 0f, 0f, 1f));
					}
				}

				var sphere = shape as SphereShape;
				if (sphere != null)
				{	
					// BUG wrong position
					DrawScreenSphere(matrix, sphere.Radius, Vector3D.Zero, new Vector4(1f, 1f, 1f, 1f));
					// Also wrong position
					Drawing.RoundMarker(matrix.Translation, Color.BlueViolet);
				}

				var capsule = shape as CapsuleShape;
				if (capsule != null)
				{
					DrawScreenSphere(matrix, capsule.Radius, capsule.VertexA, new Vector4(1f, 0f, 1f, 1f));
					DrawScreenSphere(matrix, capsule.Radius, capsule.VertexB, new Vector4(1f, 0f, 1f, 1f));
				}
			}
		}

		private static void DrawScreenSphere(MatrixD matrix, float radius, Vector3D localCenter, Vector4 color)
		{
			var camera = MyAPIGateway.Session.Camera;
			Vector3D worldCenter = Vector3D.Transform(localCenter, matrix);
			Vector3D viewDir = Vector3D.Normalize(worldCenter - camera.Position);

			Vector3D right, localUp;
			GetOrthonormalBasis(viewDir, out right, out localUp);

			int segments = 64;
			var silhouettePoints = new List<Vector3D>();
			for (int i = 0; i < segments; i++)
			{
				double angle = i * MathHelper.TwoPi / segments;
				Vector3D worldPoint = worldCenter + Math.Cos(angle) * right * radius + Math.Sin(angle) * localUp * radius;
				silhouettePoints.Add(worldPoint);
			}
			var projected = Drawing.WorldToScreen(silhouettePoints);
			if (projected.Count >= 2)
				Drawing.Contour(projected.ToArray(), true, 5e-5f, color);
		}

		private static void GetOrthonormalBasis(Vector3 axis, out Vector3 right, out Vector3 up)
		{
			var perp = Math.Abs(Vector3.Dot(axis, Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
			right = Vector3.Normalize(Vector3.Cross(axis, perp));
			up = Vector3.Cross(right, axis);
		}

		private static void GetOrthonormalBasis(Vector3D axis, out Vector3D right, out Vector3D up)
		{
			var perp = Math.Abs(Vector3D.Dot(axis, Vector3D.Up)) > 0.99 ? Vector3D.Forward : Vector3D.Up;
			right = Vector3D.Normalize(Vector3D.Cross(axis, perp));
			up = Vector3D.Cross(right, axis);
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
					var he = box.HalfExtents;
					var verts = new List<Vector3>
					{
						new Vector3( he.X,  he.Y,  he.Z),
						new Vector3( he.X,  he.Y, -he.Z),
						new Vector3( he.X, -he.Y,  he.Z),
						new Vector3( he.X, -he.Y, -he.Z),
						new Vector3(-he.X,  he.Y,  he.Z),
						new Vector3(-he.X,  he.Y, -he.Z),
						new Vector3(-he.X, -he.Y,  he.Z),
						new Vector3(-he.X, -he.Y, -he.Z),
					};
					for (int v = 0; v < verts.Count; ++v)
						verts[v] = Vector3.Transform(verts[v], box.Transform);
					shapes[i] = new ConvexHullShape { Vertices = verts };
				}
				var cylinder = shape as CylinderShape;
				if (cylinder != null)
				{
					var axis = Vector3.Normalize(cylinder.VertexB - cylinder.VertexA);
					Vector3 right, localUp;
					GetOrthonormalBasis(axis, out right, out localUp);
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
