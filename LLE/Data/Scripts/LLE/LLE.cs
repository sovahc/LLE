using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
			out IMyCubeGrid grid, out Vector3I position, out Vector3I freeSpace, float range = 500)
		{
			grid = null;
			position = Vector3I.Zero;
			freeSpace = Vector3I.Zero;

			IHitInfo hit;
			MyAPIGateway.Physics.CastRay(origin, origin + direction * range, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

			if (hit == null) return;

			var g = hit.HitEntity.GetTopMostParent() as IMyCubeGrid;
			if (g == null) return;

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(origin, origin + direction * range);
			g.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null) return;

			grid = g;
			position = slimBlock.Position;

			freeSpace = grid.WorldToGridInteger(origin + direction * (dist - grid.GridSize));
		}

		public static void HighlightCell(IMyCubeGrid grid, Vector3I position, Color color)
		{
			double blockSize = grid.GridSizeEnum == MyCubeSize.Large ? 2.5 : 0.5;

			Vector3D world = grid.GridIntegerToWorld(position);

			MatrixD matrix = grid.WorldMatrix;
			matrix.Translation = world;

			var v = new Vector3D(blockSize * 0.55);

			var bb = new BoundingBoxD(-v, v);

			var material = MyStringId.GetOrCompute("Square");
			MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
				MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);
		}

		public static void Tick(ref IEnumerator iter, string name)
		{
			if (iter == null) return;
			var start = Stopwatch.GetTimestamp();
			var limit = start + TimeSpan.TicksPerMillisecond / 2;
			long now = start;
			for (int i = 0; i < 100; ++i)
			{
				if (!iter.MoveNext())
				{
					(iter as IDisposable)?.Dispose();
					iter = null;
					break;
				}
				now = Stopwatch.GetTimestamp();
				if (now >= limit) break;
			}
			var ms = (now - start) / (double)TimeSpan.TicksPerMillisecond;
			MyConsole.Add($"{name}: {ms:0.##}", Color.IndianRed);
		}
	}

	/// <inheritdoc cref="Profiler" />
	/// <summary>
	/// This code was provided by Digi as a simple profiler
	/// Usage:
	///		Wrap code you want to profile in:
	///			using(new Profiler("somename"))
	///			{
	///				// code to profile
	///			}
	/// </summary>
	public struct Profiler : IDisposable
	{
		private readonly string _name;
		private readonly long _start;

		public Profiler(string name = "unnamed")
		{
			_name = name;
			_start = Stopwatch.GetTimestamp();
		}

		public override string ToString()
		{
			long end = Stopwatch.GetTimestamp();
			TimeSpan timespan = new TimeSpan(end - _start);
			return $"{_name} {timespan.TotalMilliseconds:0.###}ms";
		}

		public void Dispose() { }
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

		public static void Add(string text)
		{
			Add(text, Color.White);
		}

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
		public static void Grid_OnBlockAdded(IMySlimBlock block) { }

		public static void Grid_OnBlockRemoved(IMySlimBlock block) { }

		public static void Grid_OnGridChanged(IMyCubeGrid grid) { }
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;
		IMyCubeGrid grid_A, grid_B;
		Vector3I point_A, point_B;
		Vector3I selectedBlock;
		AStar astar;
		const int border = 1;

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
			Collisions.Load(ModContext);

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
				Utilities.MyRaycast(pm.Translation, pm.Forward, out grid_A, out selectedBlock, out point_A);

				if(grid_A != null)
				{	var block = grid_A.GetCubeBlock(selectedBlock);
					if(block != null)
						MyConsole.Add($"Id {block.BlockDefinition.Id}", Color.Wheat);
				}
			}

			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				Utilities.MyRaycast(pm.Translation, pm.Forward, out grid_B, out selectedBlock, out point_B);
			}

			bool mouse = MyAPIGateway.Input.IsNewLeftMousePressed() ||
				MyAPIGateway.Input.IsNewRightMousePressed();

			if (mouse && grid_A == grid_B && grid_A != null)
			{
				var grid = grid_A;
				Vector3I gridSize = grid.Max - grid.Min + 1;

				Log($"calculate_A_star {grid.Min} {grid.Max} {gridSize}");

				var astarSize = gridSize + border + border;

				if(astar == null || astar.Size != astarSize) astar = new AStar(astarSize);

				List<IMySlimBlock> blocks = new List<IMySlimBlock>();
				grid.GetBlocks(blocks);

				using(var p = new Profiler("fill"))
				{	foreach(var slim in blocks)
					{	
						var min = slim.Min;
						var max = slim.Max;

						if(min == max)
						{	astar.SetWeight(slim.Position - grid.Min + border, 255);
						}
					}
					MyConsole.Add($"{p}", Color.IndianRed);
				}

				var a = point_A - grid.Min + border;
				var b = point_B - grid.Min + border;
				astar.Reset(false);
				astar.RunCalculation(a, b);
			}

			if(astar != null && !astar.Completed())
				astar.Iteration();
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			var pm = ch.GetHeadMatrix(false);

			Common.StartFrame();

			var lp = LLE_Loader.IsPresent();
			font.String("LLE_Loader.IsPresent: " + lp.ToString(),
				new Vector2D(0.5, -0.97), 0.00075f, lp ? Color.White : Color.Red);

			if(grid_A != null && astar != null && astar.Completed())
			{	var path = astar.result;
				for (int p = 0; p < astar.result.Count; ++p)
				{	var v = path[p] + grid_A.Min - border;
					Drawing.RoundMarker(grid_A.GridIntegerToWorld(v), Color.Yellow);
				}
			}

			if (grid_A != null && point_A != null) Utilities.HighlightCell(grid_A, point_A, Color.Green);
			if (grid_A != null && selectedBlock != null)
			{	var block = grid_A.GetCubeBlock(selectedBlock);
				
				if (block != null) Collisions.Draw(grid_A, block);

			}
			if (grid_B != null && point_B != null) Utilities.HighlightCell(grid_B, point_B, Color.Red);

			if (grid_A != null && selectedBlock != null)
			{
				var block = grid_A.GetCubeBlock(selectedBlock);
				if (block != null)
				{
					bool intersects = CheckProbeVsBlock(block);
					List<Vector3> probeVerts = new List<Vector3>();
					Collisions.LongEngineerTestProbe(probeVerts);

					Matrix bo;
					block.Orientation.GetMatrix(out bo);
					Quaternion q = Quaternion.CreateFromRotationMatrix(grid_A.WorldMatrix);
					Matrix.Transform(ref bo, ref q, out bo);
					var blockCenter = 0.5 * (grid_A.GridIntegerToWorld(block.Min) + grid_A.GridIntegerToWorld(block.Max));
					MatrixD blockMatrix = new MatrixD(bo) { Translation = blockCenter };

					var worldProbeVerts = probeVerts.Select(v => Vector3D.Transform(new Vector3D(v), blockMatrix)).ToList();
					var screenProbeVerts = Drawing.WorldToScreen(worldProbeVerts);
					var hull = Geometry.ConvexHull(screenProbeVerts);
					var probeColor = intersects ? Color.Red : Color.Green;
					Drawing.Contour(hull.ToArray(), true, 5e-5f, new Vector4(probeColor.R / 255f, probeColor.G / 255f, probeColor.B / 255f, probeColor.A / 255f));
				}
			}
			MyConsole.Render(font);

			Common.Call_Add_Billboards(); // just for sure
		}

		void OnEntityAdd(IMyEntity entity)
		{
			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnClose += OnClose;

				grid.OnBlockAdded += Vision.Grid_OnBlockAdded;
				grid.OnBlockRemoved += Vision.Grid_OnBlockRemoved;
				grid.OnGridChanged += Vision.Grid_OnGridChanged;
			}
		}

		void OnClose(IMyEntity entity)
		{
			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded -= Vision.Grid_OnBlockAdded;
				grid.OnBlockRemoved -= Vision.Grid_OnBlockRemoved;
				grid.OnGridChanged -= Vision.Grid_OnGridChanged;
			}
		}

		bool CheckProbeVsBlock(IMySlimBlock block)
		{
			if(block == null) return false;

			CollisionGeometry geometry;
			if (!Collisions._collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry))
				return false;

			List<Vector3> probeVerts = new List<Vector3>();
			Collisions.LongEngineerTestProbe(probeVerts);

			foreach (var shape in geometry.Shapes)
			{
				var convex = shape as ConvexHullShape;
				if (convex != null)
				{
					var localVerts = convex.Vertices.Select(v => Vector3.Transform(v, shape.Transform)).ToList();
					if (Intersections.ConvexIntersectsConvex(probeVerts, localVerts))
						return true;
				}
			}
			return false;
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
