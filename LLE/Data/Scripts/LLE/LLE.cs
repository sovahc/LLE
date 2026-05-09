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
			Utilities.Log(text);

			_lines.Add(new LineData { Text = text, ColorIndex = (int)color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Draw(TextRendering font)
		{
			if (font == null || _lines.Count == 0) return;

			float B = 0.01f;
			font.DrawRectangle(new Vector2(-1+B, B), new Vector2(-0.5f-B, 1f-B),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);

			float scale = 0.00075f;
			float lineStep = 0.025f;

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
		private static SocketClient _socket = new SocketClient();
		private static readonly byte[] _rxBuffer = new byte[4096];

		public static void Log(string s) { Utilities.Log(s); }

		public override void UpdateBeforeSimulation()
		{
			_socket.Update();

			int bytes = _socket.Receive(_rxBuffer, _rxBuffer.Length);
			if (bytes > 0)
			{
				string msg = System.Text.Encoding.UTF8.GetString(_rxBuffer, 0, bytes);
				MyConsole.Log("RX: " + msg.Trim(), Palette.Yellow);
			}
		}
		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			_font = new TextRendering();
			if (_font.Parse(@"Fonts\monospace\FontDataPA.xml"))
			{
				_font.LoadAtlas("LLE_monospace2048");
			}
			else
			{
				Log("DBG: Failed to parse font!");
			}

			//_nextMessage = MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds + 5.0;
	        MyConsole.Log("LLE_Loader.IsPresent: " + LLE_Loader.IsPresent().ToString());
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);
			Vision.HighlightVisible(p.Translation, p.Forward);

			_font?.StartFrame();
			_font?.DrawString("LLE v0.2 ☻", new Vector2D(-0.5d, -0.35d), 0.00075f, Color.White);

			MyConsole.Draw(_font);
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;

		public static bool Connect() => false;
		public static void Disconnect() { }
		public static bool Send(byte[] data, int length) => false;
		public static int Receive(byte[] buffer, int maxLength) => 0;
	}

	class SocketClient
	{
		private double _nextReconnectTime;
		private float _reconnectDelay = 0.5f;
		private const float MaxReconnectDelay = 10f;

		public bool IsConnected = false;

		double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;

		public void Update()
		{
			if (!IsConnected && Now >= _nextReconnectTime)
			{
				IsConnected = LLE_Loader.Connect();
				if (IsConnected)
				{	LLE.Log("SocketClient.Connected");
					ResetBackoff();
				}
			}

			if (IsConnected)
			{
				// Check if socket is still alive by attempting a non-blocking receive probe
				int bytes = LLE_Loader.Receive(null, 0);
				if (bytes < 0) HandleDisconnect();
			}
		}

		public bool Send(byte[] data, int length)
		{
			if (!IsConnected) return false;
			bool ok = LLE_Loader.Send(data, length);
			if (!ok) HandleDisconnect();
			return ok;
		}

		public int Receive(byte[] buffer, int maxLength)
		{
			if (!IsConnected || buffer == null) return 0;
			int bytes = LLE_Loader.Receive(buffer, maxLength);
			if (bytes < 0) HandleDisconnect();
			return Math.Max(0, bytes);
		}

		private void HandleDisconnect()
		{
			LLE.Log("SocketClient.HandleDisconnect");

			LLE_Loader.Disconnect();
			IsConnected = false;

			_nextReconnectTime = Now + _reconnectDelay;
			_reconnectDelay = Math.Min(_reconnectDelay * 2, MaxReconnectDelay);
		}

		public void ResetBackoff()
		{
			_reconnectDelay = 0.5f;
		}
	}
}
