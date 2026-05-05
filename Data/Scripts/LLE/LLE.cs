using System;
using System.Collections.Generic;

using Sandbox.Definitions;
using Sandbox.Game;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;

using VRageMath;

using ProtoBuf;
using System.Linq;
using Sandbox.ModAPI.Weapons;

using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;
using Sandbox.Common.ObjectBuilders;

namespace LargeLanguageEngineer
{
	class Utilities
	{
		public static void Log(string s)
		{
			MyLog.Default.WriteLine("LLE " + s);
		}

		public static void LogException(string text, string subject, Exception e)
		{
			MyLog.Default.WriteLine($"LLE {text} '{subject}' \n{e.Message}\n\n{e.StackTrace}");
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

		//private bool InRange(float v, float min, float max) { return v >= min && v <= max; }
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		public static void Log(string s) { Utilities.Log(s); }

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{	Log("Init");
		}
		protected override void UnloadData() {}
		public override void SaveData() {}
		public override void LoadData() {}
		public override void BeforeStart() {}
		public override void UpdateBeforeSimulation() {}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var m = player.Character.GetHeadMatrix(false);
			var a = m.Translation;
			var f = m.Forward * 50;

			IHitInfo hitInfo;

			MyAPIGateway.Physics.CastRay(a, a+f, out hitInfo, CollisionLayers.VoxelCollisionLayer);

			if (hitInfo != null)
			{	
				var color = new Color(127, 255, 255, 255);
				Utilities.DrawPoint(a + f * hitInfo.Fraction, color);
			}
		}
	}
}
