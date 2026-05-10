using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
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

		private static readonly Color[] MyColors = {
			Color.White,
			Color.Gray,
			Color.Silver,
			Color.Red,
			Color.Yellow,
			Color.Blue
		};

		private static readonly Color textBackground = new Color(0, 0, 0, 127);

		public static void Add(string text, Palette color = global::LLE.Palette.Default)
		{
			Utilities.Log(text);

			_lines.Add(new LineData { Text = text, ColorIndex = (int)color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Clear()
		{	_lines.Clear();			
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
				font.DrawString(line.Text, new Vector2D(-0.99f, y), scale, MyColors[line.ColorIndex]);
			}
		}
	}

	class Vision
	{
		class LastKnownState
		{
			public string DisplayName;
			public Vector3D Position;
		}

		private static readonly Dictionary<long, LastKnownState> lks = new Dictionary<long, LastKnownState>();
		private const double minimalPositionDelta = 0.05;

		public static void HighlightVisible(Vector3D at, float range = 1000)
		{
			BoundingSphereD pruneSphere = new BoundingSphereD(at, range);

			var candidates = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref pruneSphere);

			foreach (var entity in candidates)
			{
				if(entity.Closed) continue;

				LastKnownState state;
				if(lks.TryGetValue(entity.EntityId, out state))
				{
					if((state.Position - entity.GetPosition()).LengthSquared() >
						minimalPositionDelta*minimalPositionDelta)
					{	state.Position = entity.GetPosition();

						MyConsole.Add($"POS {state.DisplayName} {state.Position}", Palette.Silver);
					}
				}
				else
				{
                    state = new LastKnownState
                    {
                        DisplayName = entity.DisplayName,
                        Position = entity.GetPosition()
                    };

                    lks.Add(entity.EntityId, state);

					MyConsole.Add($"ADD {state.DisplayName} {state.Position}", Palette.Yellow);
				}

				/*Vector3D targetPos = entity.PositionComp.WorldMatrixRef.Translation;
				Utilities.DrawPoint(targetPos);
				var distance = (entity.WorldMatrix.Translation - at).Length();

				IMyCubeGrid grid = entity as IMyCubeGrid;
				if (grid != null)
				{	if(grid.Physics == null) continue;
					//if (mgrid.Physics == null && !mgrid.IsStatic)
					MyConsole.Log($"G: {entity.DisplayName} {entity.Name} {distance}");
					continue;
				}
				IMyFloatingObject fo = entity as IMyFloatingObject;
				if (fo != null)
				{	MyConsole.Log($"fo: {entity.DisplayName} {entity.Name} {distance}");
					continue;
				}
				MyVoxelBase vb = entity as MyVoxelBase;
				if (vb != null)
				{	MyConsole.Log($"fo: {entity.DisplayName} {entity.Name} {distance}");
					continue;
				}

				MyCubeBlock cb = entity as MyCubeBlock;
				if (cb != null)
				{	MyConsole.Log($"cb: {entity.DisplayName} {entity.Name} {distance}");
					continue;
				}

				IMyCharacter ch = entity as IMyCharacter;
				if (ch != null)
				{	MyConsole.Log($"character: {entity.DisplayName} {entity.Name} {distance}");
					continue;
				}

				MyConsole.Log($"UNK: {entity.DisplayName} {entity.Name} {distance}", Palette.Red);
				*/
			}
		}

		public static void OnClose(IMyEntity e)
		{	MyConsole.Add($"REM {e.DisplayName} {e.GetPosition()}", Palette.Blue);
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

			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var headMatrix = player.Character.GetHeadMatrix(false);
			var dto = new PlayerStateDto
			{
				PositionX = headMatrix.Translation.X,
				PositionY = headMatrix.Translation.Y,
				PositionZ = headMatrix.Translation.Z
			};

			byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(dto);
			int totalLength = 4 + payload.Length;
			byte[] frame = new byte[totalLength];
			frame[0] = (byte)(payload.Length & 0xFF);
			frame[1] = (byte)((payload.Length >> 8) & 0xFF);
			frame[2] = (byte)((payload.Length >> 16) & 0xFF);
			frame[3] = (byte)((payload.Length >> 24) & 0xFF);
			System.Array.Copy(payload, 0, frame, 4, payload.Length);
			_socket.Send(frame, totalLength);
		}
		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			_font = new TextRendering();

			if (!_font.Load(@"Fonts\monospace\FontDataPA.xml", "LLE_monospace2048"))
				Log("DBG: Failed to parse font!");
		}

		public override void Draw()
		{
			_font?.StartFrame();

			var lp = LLE_Loader.IsPresent();
			_font?.DrawString("LLE_Loader.IsPresent: " + lp.ToString(),
				new Vector2D(0, -0.35d), 0.00075f, lp ? Color.White : Color.Red);

			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);
			Vision.HighlightVisible(p.Translation);

			MyConsole.Draw(_font);
		}

		public override void BeforeStart()
    	{	MyEntities.OnEntityAdd += OnEntityAdd;
		}

		protected override void UnloadData()
		{	MyEntities.OnEntityAdd -= OnEntityAdd;
		}

		void OnEntityAdd(IMyEntity entity)
		{	entity.OnClose += Vision.OnClose;
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
