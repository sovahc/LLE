using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
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
using static VRageRender.MyBillboard;

namespace LLE
{

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

		public static void DrawAABB(MatrixD worldMatrix, BoundingBox localBB, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.002f)
		{
			DrawAABB(worldMatrix, new BoundingBoxD(localBB.Min, localBB.Max), color, raster, thickness);
		}

		public static void DrawAABB(MatrixD worldMatrix, BoundingBoxD localBB, Color color, MySimpleObjectRasterizer raster = MySimpleObjectRasterizer.Wireframe, float thickness = 0.002f)
		{
			var material = MyStringId.GetOrCompute("Square");
			Vector3D centerLocal = (localBB.Min + localBB.Max) * 0.5;
			Vector3D extentsLocal = (localBB.Max - localBB.Min) * 0.5;
			var worldCenter = Vector3D.Transform(centerLocal, ref worldMatrix);
			
			MatrixD drawMatrix = MatrixD.CreateFromQuaternion(QuaternionD.CreateFromRotationMatrix(worldMatrix));
			drawMatrix.Translation = worldCenter;
			
			var bbD = new BoundingBoxD(-extentsLocal, extentsLocal);
			MySimpleObjectDraw.DrawTransparentBox(ref drawMatrix, ref bbD, ref color, raster, 1, thickness, material, material);
		}

		public static void HighlightVisible(SocketClient socket, Vector3D rayOrigin, Vector3D rayDir, float range = 1000)
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
					DrawAABB(grid.WorldMatrix, grid.PositionComp.LocalAABB, intersects ? Color.Magenta : Color.Red);
				}

				var voxel = entity as MyVoxelBase;
				if (voxel != null)
				{
					if (voxel is MyPlanet) continue;

					var size = voxel.SizeInMetres;
					var box = new BoundingBoxD(-size/2, size/2);
					bool intersects = Ellipsoid.RayIntersectsEllipsoid(rayOrigin, rayDir, voxel.WorldMatrix, box);
					DrawAABB(voxel.WorldMatrix, box, intersects ? Color.Magenta : Color.Yellow);
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
			int totalLength = 4 + payload.Length;
			byte[] frame = new byte[totalLength];
			frame[0] = (byte)(payload.Length & 0xFF);
			frame[1] = (byte)((payload.Length >> 8) & 0xFF);
			frame[2] = (byte)((payload.Length >> 16) & 0xFF);
			frame[3] = (byte)((payload.Length >> 24) & 0xFF);
			System.Array.Copy(payload, 0, frame, 4, payload.Length);
			socket.Send(frame, totalLength);
		}

		public static void Send(SocketClient sc, bool changedOnly)
		{	foreach(var state in lks.Values)
			{	if(changedOnly && !state.Changed) continue;
				state.Changed = false;

				SendState(sc, state);
			}
		}
	}

	class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Drawing draw;
		private static SocketClient _socket = new SocketClient();

		public static void Log(string s) { Utilities.Log(s); }

		public override void UpdateBeforeSimulation()
		{
			bool before = _socket.IsConnected;
			_socket.Update();
			bool after = _socket.IsConnected;

			if(!before && after) Vision.Send(_socket, false);
			else if(after) Vision.Send(_socket, true);
		}
		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			draw = new Drawing();

			if (!draw.LoadFont(@"Fonts\monospace\FontDataPA.xml", "LLE_monospace2048"))
				Log("DBG: Failed to parse font!");
		}

		public override void Draw()
		{
			draw?.StartFrame();

			const int circleSegments = 64;
			var circlePoints = new Vector2D[circleSegments];
			for (int i = 0; i < circleSegments; i++)
			{
				double angle = i * Math.PI * 2.0 / circleSegments;
				circlePoints[i] = new Vector2D(Math.Cos(angle) * 0.3, Math.Sin(angle) * 0.3);
			}
			draw?.Contour(circlePoints, true, 5e-5f, new Vector4(1, 0, 0, 1));

			var lp = LLE_Loader.IsPresent();
			draw?.String("LLE_Loader.IsPresent: " + lp.ToString(),
				new Vector2D(0, -0.35d), 0.00075f, lp ? Color.White : Color.Red);

			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			var p = player.Character.GetHeadMatrix(false);
			Vision.HighlightVisible(_socket, p.Translation, p.Forward);

			MyConsole.Render(draw);
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
				LLE.Log("SocketClient: connecting...");

				IsConnected = LLE_Loader.Connect();
				if (IsConnected)
				{	LLE.Log("SocketClient: connected");
					ResetBackoff();
				}
				else
				{	IncreaseBackoff();
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
			LLE.Log("SocketClient: disconnect");

			LLE_Loader.Disconnect();
			IsConnected = false;
			
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
