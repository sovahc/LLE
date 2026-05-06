using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;

using VRageMath;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LargeLanguageEngineer
{
	class Vision
	{
		private static readonly float FovAngle = (float)Math.PI / 6;
		private static readonly float Tan_HalfFovAngle = (float)Math.Tan(FovAngle/2);
		private static readonly float Cos_HalfFovAngle = (float)Math.Cos(FovAngle/2);

		public static void HighlightVissible(Vector3D at, Vector3D forward, float range = 5000)
		{
			BoundingBoxD searchBox;
			{
				Vector3D center = at + forward * (range / 2);
				float radius = Math.Max(range / 2, range * Tan_HalfFovAngle);

				searchBox = new BoundingBoxD(center - new Vector3(radius), center + new Vector3(radius));
			}

			var candidates = MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref searchBox);

			//Log($"{botPos} {botForward} {candidates.Count}");

			foreach (var entity in candidates)
			{
				IMyCubeGrid grid = entity as IMyCubeGrid;

				if (grid == null) continue;
				if (grid.Physics == null) continue; // grid is a projection

				Vector3D targetPos = entity.PositionComp.WorldMatrixRef.Translation;
				Vector3D direction = targetPos - at;

				if (direction.LengthSquared() > range * range) continue;

				double dot = Vector3D.Dot(Vector3D.Normalize(direction), forward);
				if (dot < Cos_HalfFovAngle) continue;

				Utilities.DrawPoint(targetPos);

				//MyAPIGateway.Physics.CastRay(botPos, targetPos, out var hit);
				//if (hit.HitEntity == null || hit.HitEntity.EntityId == entity.EntityId)
				//{
				//	BotSeesTarget(entity);
				//}
			}
		}
	}

	class Utilities
	{
		private static Color DefaultColor = new Color(255, 255, 127, 255);

		public static void DrawPoint(Vector3D point)
		{	DrawPoint(point, DefaultColor);
		}

		public static void DrawPoint(Vector3D point, Color color)
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) return;

			var cameraMatrix = camera.WorldMatrix;

			var material = MyStringId.GetOrCompute("LLE-Marker");

			Vector3D viewDir = Vector3D.Normalize(point - camera.Position);
			var distance = (point - camera.Position).Normalize();

			point = camera.Position + viewDir;

			float size = (float)(0.25 / (distance + 0.0001));
			if (size < 0.001f) size = 0.001f;
			if (size > 0.25f) size = 0.25f;

			MyTransparentGeometry.AddBillboardOriented(material, color, point, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, radius: size);
		}

		public static void Log(string s)
		{
			MyLog.Default.WriteLine("LLE " + s);
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		public static void Log(string s) { Utilities.Log(s); }

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);

			Vision.HighlightVissible(p.Translation, p.Forward);
		}
	}
}
