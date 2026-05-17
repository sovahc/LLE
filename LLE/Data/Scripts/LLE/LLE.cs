using System;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

		public static void MyRaycast(Vector3D origin, Vector3D direction,
			out IMyCubeGrid grid, out Vector3I position,
			float range = 1000)
		{
			grid = null;
			position = Vector3I.Zero;

			IHitInfo hit;
			MyAPIGateway.Physics.CastRay(origin, origin + direction * range, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

			if (hit == null) return;

			grid = hit.HitEntity.GetTopMostParent() as IMyCubeGrid;
			if (grid == null) return;

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(origin, origin + direction * range);
			grid.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null) return;

			var fsCenter = origin + direction * (dist - grid.GridSize);
			var freeSpace = grid.WorldToGridInteger(fsCenter);
			position = freeSpace;
		}

		public static void HighlightCell(IMyCubeGrid grid, Vector3I position, Color color)
		{
			double blockSize = grid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 0.5;

			Vector3D world = grid.GridIntegerToWorld(position);

			MatrixD matrix = grid.WorldMatrix;
			matrix.Translation = world;

			var v = new Vector3D(blockSize * 0.55);
			//Drawing.AABB(matrix, new BoundingBoxD(-v, v), color, 0.01f);

			var bb = new BoundingBoxD(-v, v);

			var material = MyStringId.GetOrCompute("Square");
			MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
				MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);

			Drawing.RoundMarker(world, color);
		}
	}

	public struct Profiler
	{
		private readonly string _name;
		private readonly long _start;
		private long _end;
		public Profiler(string name = "unnamed")
		{
			_name = name;
			_start = Stopwatch.GetTimestamp();
			_end = long.MaxValue;
		}
		public void Stop()
		{	_end = Stopwatch.GetTimestamp();
		}
		public override string ToString()
		{	return $"{_name} {new TimeSpan(_end - _start).TotalMilliseconds:0.##}ms";
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
			//Utilities.Log(text);
			_lines.Add(new LineData { Text = text, Color = color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void Clear()
		{
			_lines.Clear();
		}

		public static void Render(Font font)
		{
			if (_lines.Count == 0) return;

			float B = 0.01f;
			float scale = 0.00075f;
			float lineStep = font.GetHeight(scale) * 1.2f;

			float y0 = 0;
			float x0 = -0.99f;
			float rectangleH = _lines.Count * lineStep;
			float rectangleW = 0;

			for (int i = 0; i < _lines.Count; ++i)
			{
				var line = _lines[_lines.Count - i - 1];
				float y = y0 + i * lineStep;
				var w = font.String(line.Text, new Vector2D(x0, y), scale, line.Color);
				if (w > rectangleW) rectangleW = w;
			}

			var bb = font.Rectangle(new Vector2(x0 - B, y0 - B),
				new Vector2(x0 + rectangleW + B + B, y0 + rectangleH + B + B),
				MyStringId.GetOrCompute("Square"),
				Vector2.Zero, Vector2.One, textBackground);

			MyTransparentGeometry.AddBillboard(bb, false);
			Common.Call_Add_Billboards();
		}
	}

	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	class Vision
	{

		public static void OnClose(IMyEntity e) { }

		public static void Grid_OnBlockAdded(IMySlimBlock block) { }

		public static void Grid_OnBlockRemoved(IMySlimBlock block) { }

		public static void Grid_OnGridChanged(IMyCubeGrid grid) { }
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;

		public static void Log(string s) { Utilities.Log(s); }

		public override void Init(MyObjectBuilder_SessionComponent sessionComponent)
		{
			Log("Init");

			font = new Font();

			if (!font.LoadFont(@"Fonts\monospace\FontDataPA.xml", "LLE_monospace2048"))
				Log("ERROR: Failed to parse font!");
		}

		public override void BeforeStart()
		{
			var entities = new HashSet<IMyEntity>();
			MyAPIGateway.Entities.GetEntities(entities);

			foreach (var e in entities) OnEntityAdd(e);

			MyEntities.OnEntityAdd += OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered += OnChatMessage;
		}

		protected override void UnloadData()
		{
			MyEntities.OnEntityAdd -= OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered -= OnChatMessage;
		}

		public override void UpdateBeforeSimulation()
		{
			LLE_Loader.Update();

			//if (LLE_Loader.IsPresent())
			//	LLE_Loader.SetVision(Vision.lks);

			ServerCommand cmd;
			if (LLE_Loader.GetCommand(out cmd))
			{	////
			}

			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			var pm = ch.GetHeadMatrix(false);

			if (MyAPIGateway.Input.IsNewLeftMousePressed())
			{
			}
			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
			}
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null || player.Character == null) return;

			Common.StartFrame();

			var lp = LLE_Loader.IsPresent();
			font.String("LLE_Loader.IsPresent: " + lp.ToString(),
				new Vector2D(0.5, -0.97), 0.00075f, lp ? Color.White : Color.Red);

			var pm = player.Character.GetHeadMatrix(false);
			//Vision.HighlightVisible(pm.Translation, pm.Forward);

			MyConsole.Render(font);

			Common.Call_Add_Billboards(); // just for sure
		}

		void OnEntityAdd(IMyEntity entity)
		{
			entity.OnClose += Vision.OnClose;

			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded += Vision.Grid_OnBlockAdded;
				grid.OnBlockRemoved += Vision.Grid_OnBlockRemoved;
				grid.OnGridChanged += Vision.Grid_OnGridChanged;
			}
		}

		void OnChatMessage(string message, ref bool sendToOthers)
		{
			if (!LLE_Loader.IsPresent()) return;
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;

			LLE_Loader.SetChat(player.DisplayName, message);
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;
		public static void Update() { }
		public static void SetVision(Dictionary<long, LastKnownState> states) { }
		public static void SetChat(string author, string text) { }
		public static bool GetCommand(out ServerCommand cmd) { cmd = null; return false; }
	}
}
