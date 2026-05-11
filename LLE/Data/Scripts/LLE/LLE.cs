using System;
using System.IO;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using ProtoBuf;

using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

using VRageMath;

namespace LLE
{
	class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

		public static void SendFrame(SocketClient sc, MsgType type, byte[] payload) {
			if(!sc.IsConnected || payload == null) return;
			int len = payload.Length;
			int total = 3 + len;
			byte[] frame = new byte[total];
			frame[0] = (byte)(len & 0xFF);
			frame[1] = (byte)((len >> 8) & 0xFF);
			frame[2] = (byte)type;
			System.Array.Copy(payload, 0, frame, 3, len);
			sc.Send(frame, total);
		}
	}

	class MyConsole
	{
		struct LineData
		{
			public string Text;
			public Color Color;
		}

		private static readonly List<LineData> _lines = new List<LineData>();
		const int MaxLines = 50;

		private static readonly Color textBackground = new Color(0, 0, 0, 127);

		public static void Add(string text, Color color)
		{
			Utilities.Log(text);

			_lines.Add(new LineData { Text = text, Color = color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Clear()
		{	_lines.Clear();			
		}

		public static void Render(Drawing draw)
		{
			if (draw == null || _lines.Count == 0) return;

			float B = 0.01f;
			draw.Rectangle(new Vector2(-1+B, B), new Vector2(-0.5f-B, 1f-B),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);

			float scale = 0.00075f;
			float lineStep = 0.025f;

			for (int i = 0; i < _lines.Count; ++i)
			{
				var line = _lines[_lines.Count - i - 1];
				float y = 0.05f + i * lineStep;
				draw.String(line.Text, new Vector2D(-0.99f, y), scale, line.Color);
			}
		}
	}

	class Vision
	{
		private static readonly Dictionary<long, LastKnownState> lks = new Dictionary<long, LastKnownState>();
		private const double minimalPositionDelta = 0.05;

		private static void SetFromEntity(LastKnownState s, IMyEntity e)
		{	s.DisplayName = e.DisplayName;
			var p = e.GetPosition();
			s.X = p.X;
			s.Y = p.Y;
			s.Z = p.Z;
			s.Changed = true;
		}

		private static double DistanceSquared(LastKnownState s, IMyEntity e)
		{	return (e.GetPosition() - new Vector3(s.X, s.Y, s.Z)).LengthSquared();
		}

		public static void HighlightVisible(Drawing draw, SocketClient socket, Vector3D rayOrigin, Vector3D rayDir, float range = 1000)
		{
			BoundingSphereD pruneSphere = new BoundingSphereD(rayOrigin, range);

			var candidates = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref pruneSphere);

			foreach (IMyEntity entity in candidates)
			{
				if(entity.Closed) continue;

				var grid = entity as IMyCubeGrid;
				if (grid != null)
				{
					bool intersects = Ellipsoid.RayIntersectsEllipsoid(rayOrigin, rayDir, grid.WorldMatrix, grid.PositionComp.LocalAABB);
					Drawing.AABB(grid.WorldMatrix, grid.PositionComp.LocalAABB, intersects ? Color.Magenta : Color.Red);
					draw.EllipsoidContour(grid.WorldMatrix, grid.PositionComp.LocalAABB, intersects ? Color.Cyan : Color.Gray);
				}

				var voxel = entity as MyVoxelBase;
				if (voxel != null)
				{
					if (voxel is MyPlanet) continue;

					var size = voxel.SizeInMetres;
					var box = new BoundingBoxD(-size/2, size/2);
					bool intersects = Ellipsoid.RayIntersectsEllipsoid(rayOrigin, rayDir, voxel.WorldMatrix, box);
					Drawing.AABB(voxel.WorldMatrix, box, intersects ? Color.Magenta : Color.Yellow);
					draw.EllipsoidContour(voxel.WorldMatrix, box, intersects ? Color.Cyan : Color.Gray);
				}

				LastKnownState state;
				if(lks.TryGetValue(entity.EntityId, out state))
				{
					if(DistanceSquared(state, entity) >
						minimalPositionDelta*minimalPositionDelta)
					{ 	
						SetFromEntity(state, entity);
						MyConsole.Add($"POS {state.DisplayName} {state.Position()}", Color.Silver);
					}
				}
				else
				{
					state = new LastKnownState();
					SetFromEntity(state, entity);

					lks.Add(entity.EntityId, state);
					MyConsole.Add($"ADD {state.DisplayName} {state.Position()}", Color.Yellow);
				}
			}
		}

		public static void OnClose(IMyEntity e)
		{	MyConsole.Add($"REM {e.DisplayName} {e.GetPosition()}", Color.Blue);
			//SendState(socket, state); ///////////////////////
		}

		private static void SendState(SocketClient socket, LastKnownState state)
		{	byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(state);
			Utilities.SendFrame(socket, MsgType.Vision, payload);
		}

		public static void Send(SocketClient sc, bool changedOnly)
		{	foreach(var state in lks.Values)
			{	if(changedOnly && !state.Changed) continue;
				state.Changed = false;

				SendState(sc, state);
			}
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Drawing draw;
		private static SocketClient _socket = new SocketClient();

		private byte[] _header = new byte[3];
		private byte[] _data = new byte[0x10000];
		int _headerLength;
		int _dataLength;

		public static void Log(string s) { Utilities.Log(s); }

		public override void UpdateBeforeSimulation()
		{
			bool before = _socket.IsConnected;
			_socket.Update();
			bool after = _socket.IsConnected;

			if(!before && after) Vision.Send(_socket, false);
			else if(after) Vision.Send(_socket, true);

			if (after) ProcessIncoming();
		}

		void ProcessIncoming() {
			
			int need = _header.Length;

			if(_headerLength < need)
			{	var r = _socket.Receive(_header, _headerLength, need-_headerLength);
				if(r <= 0) return;
				_headerLength += r;
			}
			if(_headerLength < need) return;

			need = _header[0] | (_header[1] << 8);
			
			if(_dataLength < need)
			{	var r = _socket.Receive(_data, _dataLength, need-_dataLength);
				if(r <= 0) return;
				_dataLength += r;				
			}
			if(_dataLength < need) return;
			
			byte[] payload = new byte[_dataLength];
			Array.Copy(_data, 0, payload, 0, _dataLength);
			HandleMessage(payload);

			_headerLength = _dataLength = 0;
		}

		void HandleMessage(byte[] data)
		{
			int messageType = _header[2];
			
			if(messageType == (int)MsgType.Command)
			{	
				ServerCommand c = MyAPIGateway.Utilities.SerializeFromBinary<ServerCommand>(data);

				MyVisualScriptLogicProvider.SendChatMessage(c.Payload, "LLM", font: "Blue");
			}
			else
			{	Log($"Error: unknown message type {messageType}");
			}
		}

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			draw = new Drawing();

			if (!draw.LoadFont(@"Fonts\monospace\FontDataPA.xml", "LLE_monospace2048"))
				Log("ERROR: Failed to parse font!");
		}

		public override void Draw()
		{
			draw.StartFrame();

			var lp = LLE_Loader.IsPresent();
			draw.String("LLE_Loader.IsPresent: " + lp.ToString(),
				new Vector2D(0, -0.35d), 0.00075f, lp ? Color.White : Color.Red);

			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);
			Vision.HighlightVisible(draw, _socket, p.Translation, p.Forward);

			MyConsole.Render(draw);
		}

		public override void BeforeStart()
		{	MyEntities.OnEntityAdd += OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered += OnChatMessage;
		}

		protected override void UnloadData()
		{	MyEntities.OnEntityAdd -= OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered -= OnChatMessage;
		}

		void OnEntityAdd(IMyEntity entity)
		{	entity.OnClose += Vision.OnClose;
		}

		void OnChatMessage(string message, ref bool sendToOthers)
		{	if(!_socket.IsConnected) return;
			var player = MyAPIGateway.Session.Player;
			if(player == null) return;
			
			var msg = new ChatMessage { Author = player.DisplayName, Text = message };
			byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(msg);
			Utilities.SendFrame(_socket, MsgType.Chat, payload);
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;

		public static bool Connect() => false;
		public static void Disconnect() { }
		public static bool Send(byte[] data, int length) => false;
		public static int Receive(byte[] buffer, int offset, int maxLength) => 0;
		public static bool IsConnected() => false;
	}

	class SocketClient
	{
		private double _nextReconnectTime;
		private float _reconnectDelay = 0.5f;
		private const float MaxReconnectDelay = 10f;

		double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;

		public bool IsConnected => LLE_Loader.IsConnected();

		public void Update()
		{
			if (!IsConnected && Now >= _nextReconnectTime)
			{
				LLE.Log("SocketClient: connecting...");

				LLE_Loader.Connect();
				if (IsConnected)
				{	LLE.Log("SocketClient: connected");
					ResetBackoff();
				}
				else
				{	IncreaseBackoff();
				}
			}
		}

		public bool Send(byte[] data, int length)
		{
			if (!IsConnected) return false;
			bool ok = LLE_Loader.Send(data, length);
			if (!ok) HandleDisconnect();
			return ok;
		}

		public int Receive(byte[] buffer, int offset, int maxLength)
		{
			if (!IsConnected || buffer == null) return 0;
			int bytes = LLE_Loader.Receive(buffer, offset, maxLength);
			if (bytes < 0) HandleDisconnect();
			if (bytes > 0) Utilities.Log($"Receive {bytes}");
			return bytes;
		}

		private void HandleDisconnect()
		{
			LLE.Log("SocketClient: disconnect");

			LLE_Loader.Disconnect();
			
			IncreaseBackoff();
		}

		public void IncreaseBackoff()
		{
			_nextReconnectTime = Now + _reconnectDelay;
			_reconnectDelay = Math.Min(_reconnectDelay * 2, MaxReconnectDelay);
		}

		public void ResetBackoff()
		{
			_reconnectDelay = 0.5f;
		}
	}
}
