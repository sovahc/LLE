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

using VRage.Game.ModAPI.Ingame.Utilities;
using System.Diagnostics.Tracing;

namespace LLE
{
	public enum Palette { Default, Gray, Silver, Red, Yellow, Blue }

	class MyConsole
	{
		struct LineData
		{
			public string Text;
			public int ColorIndex;
		}

		private static readonly List<LineData> _lines = new List<LineData>();
		const int MaxLines = 50;

		private static readonly Color[] Palette = {
			Color.White,
			Color.Gray,
			Color.Silver,
			Color.Red,
			Color.Yellow,
			Color.Blue
		};

		private static readonly Color textBackground = new Color(0, 0, 0, 100);

		public static void Log(string text, Palette color = global::LLE.Palette.Default)
		{
			_lines.Add(new LineData { Text = text, ColorIndex = (int)color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Draw(TextRendering font)
		{
			if (font == null || _lines.Count == 0) return;

			font.DrawRectangle(new Vector2(-0.5f, 0.5f), new Vector2(0.48f, -0.48f),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);

			float scale = 0.00075f;
			float lineStep = 0.02f;

			for (int i = 0; i < _lines.Count; ++i)
			{
				var line = _lines[_lines.Count - i - 1];
				float y = 0.05f + i * lineStep;
				font.DrawString(line.Text, new Vector2D(-0.99f, y), scale, Palette[line.ColorIndex]);
			}
		}
	}

	class Vision
	{
		private static readonly float FovAngle = (float)Math.PI / 6;
		private static readonly float Tan_HalfFovAngle = (float)Math.Tan(FovAngle / 2);
		private static readonly float Cos_HalfFovAngle = (float)Math.Cos(FovAngle / 2);

		public static void HighlightVisible(Vector3D at, Vector3D forward, float range = 5000)
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
				if (grid == null || grid.Physics == null) continue;

				Vector3D targetPos = entity.PositionComp.WorldMatrixRef.Translation;
				Vector3D direction = targetPos - at;

				if (direction.LengthSquared() > range * range) continue;
				double dot = Vector3D.Dot(Vector3D.Normalize(direction), forward);
				if (dot < Cos_HalfFovAngle) continue;

				Utilities.DrawPoint(targetPos);
			}
		}
	}

	class Utilities
	{
		private static Color DefaultColor = new Color(255, 255, 127, 255);

		public static void DrawPoint(Vector3D point) { DrawPoint(point, DefaultColor); }

		public static void DrawPoint(Vector3D point, Color color)
		{
			var camera = MyAPIGateway.Session.Camera;
			if (camera == null) return;

			var cameraMatrix = camera.WorldMatrix;
			var material = MyStringId.GetOrCompute("LLE-Marker");

			Vector3D viewDir = Vector3D.Normalize(point - camera.Position);
			Vector3D distance = point - camera.Position;
			point = camera.Position + viewDir;

			float size = (float)(0.25 / (distance.Length() + 0.0001));
			if (size < 0.001f) size = 0.001f;
			if (size > 0.25f) size = 0.25f;

			MyTransparentGeometry.AddBillboardOriented(material, color, point, (Vector3)cameraMatrix.Left, (Vector3)cameraMatrix.Up, radius: size);
		}

		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static TextRendering _font;

		private static double _nextFakeLogTime;
		private static readonly System.Random _random = new System.Random();

		private static readonly string[] FakePrefixes = {
			"SYS", "NET", "MEM", "CPU", "IO ", "GPU", "DMA", "IRQ",
			"PCI", "USB", "ETH", "TLS", "DNS", "FS ", "KRN", "DBG"
		};

		private static readonly string[] FakeActions = {
			"initialized", "allocated", "synced", "flushed",
			"verified", "routed", "mapped", "queued",
			"dispatched", "resolved", "bound", "committed"
		};

		public static void Log(string s) { Utilities.Log(s); }

		public override void UpdateBeforeSimulation()
		{
			double now = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
			if (now >= _nextFakeLogTime)
			{
				_nextFakeLogTime = now + 0.1 + _random.NextDouble() * 0.5;
				MyConsole.Log(GenFakeLine(), PickRandomPalette());
			}
		}

		private static string GenFakeLine()
		{
			int hexLen = 4 + _random.Next(8);
			char[] buf = new char[hexLen];
			const string hex = "0123456789ABCDEF";
			for (int i = 0; i < hexLen; i++)
				buf[i] = hex[_random.Next(hex.Length)];

			return $"[{FakePrefixes[_random.Next(FakePrefixes.Length)]}] {hexLen:X4}:{new string(buf)} " +
			       $"{FakeActions[_random.Next(FakeActions.Length)]}";
		}

		private static Palette PickRandomPalette()
		{
			var values = (Palette[])Enum.GetValues(typeof(Palette));
			return values[_random.Next(values.Length)];
		}

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			_font = new TextRendering();
			if (_font.Parse(@"Fonts\monospace\FontDataPA.xml"))
			{
				// Material IDs must match SubtypeId in TransparentMaterials.sbc
				_font.LoadAtlas("LLE_FontAtlas_0");
			}
			else
			{
				Log("DBG: Failed to parse font!");
			}

			_nextFakeLogTime = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds + 1.0;

			MyConsole.Log("System initialized");
    		MyConsole.Log("Player connected", Palette.Yellow);
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);
			Vision.HighlightVisible(p.Translation, p.Forward);

			_font?.StartFrame();
			_font?.DrawString("LLE v0.2", new Vector2D(-0.5d, -0.35d), 0.00075f, Color.White);

			MyConsole.Draw(_font);
		}
	}
}
