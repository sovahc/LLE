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

		public static void SendFrame(MsgType type, byte[] payload) {
			if(!LLE_Loader.IsConnected() || payload == null) return;
			int len = payload.Length;
			byte[] frame = new byte[3 + len];
			frame[0] = (byte)(len & 0xFF);
			frame[1] = (byte)((len >> 8) & 0xFF);
			frame[2] = (byte)type;
			System.Array.Copy(payload, 0, frame, 3, len);
			LLE_Loader.Send(frame, frame.Length);
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

		public static void HighlightVisible(Drawing draw, Vector3D rayOrigin, Vector3D rayDir, float range = 1000)
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
		}

		public static void Send(bool changedOnly)
		{	foreach(var state in lks.Values)
			{	if(changedOnly && !state.Changed) continue;
				state.Changed = false;

				byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(state);
				Utilities.SendFrame(MsgType.Vision, payload);
			}
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Drawing draw;

		private static readonly byte[] _header = new byte[3];
		private static byte[] _data;
		private static int _headerLength;
		private static int _dataLength;

		public static void Log(string s) { Utilities.Log(s); }

		public override void UpdateBeforeSimulation()
		{
			bool before = LLE_Loader.IsConnected();
			LLE_Loader.Update();
			bool after = LLE_Loader.IsConnected();

			if(!before && after)
			{	ResetParserState();
				Vision.Send(false);
			}
			else if(after)
			{	Vision.Send(true);
			}

			if (after)
			{	try
				{	ProcessIncoming();
				}
				catch(Exception e)
				{	Log($"ProcessIncoming failed with exception {e}");
					ResetParserState();
					LLE_Loader.Disconnect();
				}
			}
		}

		void ProcessIncoming()
        {
            int need = _header.Length;

            if (_headerLength < need)
            {
                var r = LLE_Loader.Receive(_header, _headerLength, need - _headerLength);
                if (r <= 0) return;
                _headerLength += r;
            }
            if (_headerLength < need) return;

            need = _header[0] | (_header[1] << 8);

            if (_data == null) _data = new byte[need];
            if (_data.Length != need) throw new Exception("code bug");

            if (_dataLength < need)
            {
                var r = LLE_Loader.Receive(_data, _dataLength, need - _dataLength);
                if (r <= 0) return;
                _dataLength += r;
            }
            if (_dataLength < need) return;

            HandleMessage();

            ResetParserState();
        }

        private static void ResetParserState()
        {
            _headerLength = _dataLength = 0;
            _data = null;
        }

        void HandleMessage()
		{
			int messageType = _header[2];
			
			if(messageType == (int)MsgType.Command)
			{	
				ServerCommand c = MyAPIGateway.Utilities.SerializeFromBinary<ServerCommand>(_data);

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
			Vision.HighlightVisible(draw, p.Translation, p.Forward);

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
		{	if(!LLE_Loader.IsConnected()) return;
			var player = MyAPIGateway.Session.Player;
			if(player == null) return;
			
			var msg = new ChatMessage { Author = player.DisplayName, Text = message };
			byte[] payload = MyAPIGateway.Utilities.SerializeToBinary(msg);
			Utilities.SendFrame(MsgType.Chat, payload);
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;

		public static void Update() { }
		public static bool Send(byte[] data, int length) => false;
		public static int Receive(byte[] buffer, int offset, int maxLength) => 0;
		public static bool IsConnected() => false;
		public static void Disconnect() { }
	}
}
