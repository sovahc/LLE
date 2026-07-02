using System.Collections.Generic;
using System.Linq;

using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	public partial class Collisions
	{	
        public static void Draw(IMySlimBlock block)
		{
			IMyCubeGrid grid = block.CubeGrid;
			CollisionGeometry geometry;
			var id = block.BlockDefinition.Id;

			if (_collisionGeometry.TryGetValue(id, out geometry))
				Draw(geometry, Transform.GetModelToWorldMatrix(block));
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
    }
}
