using System;
using System.Collections;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRage.ObjectBuilders;
using VRageMath;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

		public static bool MyRaycast(Vector3D origin, Vector3D direction,
			out IMyCubeGrid grid, out Vector3I position, float range = 1000)
		{
			grid = null;
			position = Vector3I.Zero;

			IHitInfo hit;
			MyAPIGateway.Physics.CastRay(origin, origin + direction * range, out hit, CollisionLayers.CollisionLayerWithoutCharacter);

			if (hit == null) return false;

			grid = hit.HitEntity.GetTopMostParent() as IMyCubeGrid;
			if (grid == null) return false;

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(origin, origin + direction * range);
			grid.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null) return false;

			position = slimBlock.Position;

			//var fsCenter = origin + direction * (dist - grid.GridSize);
			//var freeSpace = grid.WorldToGridInteger(fsCenter);
			//position = freeSpace;
			return true;
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

			//Drawing.RoundMarker(world, color);
			//Drawing.RoundMarker(world, color);
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

	public struct Profiler
	{
		private readonly string _name;
		private long _start;
		private long _end;
		public Profiler(string name = "unnamed")
		{
			_name = name;
			_start = Stopwatch.GetTimestamp();
			_end = long.MaxValue;
		}
		public void Start()
		{	_start = Stopwatch.GetTimestamp();
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
		private static readonly Color defultTextColor = Color.White;

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
			const string collisions_bin = "Data/collisions.bin";
			if (!MyAPIGateway.Utilities.FileExistsInModLocation(collisions_bin, ModContext.ModItem))
			{	MyConsole.Add($"ERROR: {collisions_bin} not found in mod location", Color.Red);
			}
			else
			{	using (var reader = MyAPIGateway.Utilities.ReadBinaryFileInModLocation(collisions_bin, ModContext.ModItem))
				{
					var data = reader.ReadBytes((int)reader.BaseStream.Length);
					var textDict = MyAPIGateway.Utilities.SerializeFromBinary<Dictionary<DefinitionIdAsText, CollisionGeometry>>(data);
					_collisionGeometry = new Dictionary<MyDefinitionId, CollisionGeometry>(MyDefinitionId.Comparer);
					foreach (var kv in textDict)
					{
						MyObjectBuilderType typeId;
						if (!MyObjectBuilderType.TryParse(kv.Key.TypeId, out typeId))
						{
							Utilities.Log($"Error: Failed to parse TypeId: {kv.Key.TypeId}");
							continue;
						}
						var subtypeId = MyStringHash.GetOrCompute(kv.Key.SubtypeId);
						_collisionGeometry[new MyDefinitionId(typeId, subtypeId)] = kv.Value;
					}
				}
				MyConsole.Add($"Loaded {_collisionGeometry.Count} block collisions", Color.White);
			}

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

		IMyCubeGrid grid_A, grid_B;
		Vector3I point_A, point_B;
		AStar astar;
		const int border = 1;

		private Dictionary<MyDefinitionId, CollisionGeometry> _collisionGeometry;

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

			bool pointChanged = false;
			if (MyAPIGateway.Input.IsNewLeftMousePressed())
			{
				pointChanged |= Utilities.MyRaycast(pm.Translation, pm.Forward, out grid_A, out point_A);

				if(pointChanged)
				{	var block = grid_A.GetCubeBlock(point_A);
					if(block != null)
					{	MyConsole.Add($"Id {block.BlockDefinition.Id}", Color.Wheat);
					}
				}
			}
			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				pointChanged |= Utilities.MyRaycast(pm.Translation, pm.Forward, out grid_B, out point_B);
			}

			if (pointChanged && grid_A == grid_B && grid_A != null)
			{
				var grid = grid_A;
				Vector3I gridSize = grid.Max - grid.Min + 1;

				Log($"calculate_A_star {grid.Min} {grid.Max} {gridSize}");

				var astarSize = gridSize + border + border;

				if(astar == null || astar.Size != astarSize) astar = new AStar(astarSize);

				List<IMySlimBlock> blocks = new List<IMySlimBlock>();
				grid.GetBlocks(blocks);

				Profiler p = new Profiler("fill2");
				foreach(var slim in blocks)
				{	
					var min = slim.Min;
					var max = slim.Max;

					if(min == max)
					{	astar.SetWeight(slim.Position - grid.Min + border, 255);
					}
				}
				p.Stop();
				MyConsole.Add($"{p}", Color.IndianRed);

				var a = point_A - grid.Min + border;
				var b = point_B - grid.Min + border;
				astar.Reset(false);
				astar.RunCalculation(a, b);
			}

			if(astar != null && !astar.Completed())
				astar.Iteration();

			//if ()
			//trav.Iteration();
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
			//trav.DrawDebug();

			if(grid_A != null && astar != null && astar.Completed())
			{	var path = astar.result;
				for (int p = 0; p < astar.result.Count; ++p)
				{	var v = path[p] + grid_A.Min - border;
					Drawing.RoundMarker(grid_A.GridIntegerToWorld(v), Color.Yellow);
				}
			}

			if (grid_A != null && point_A != null) Utilities.HighlightCell(grid_A, point_A, Color.Green);
			if (grid_A != null && point_A != null && _collisionGeometry != null)
			{	var block = grid_A.GetCubeBlock(point_A);
				if (block != null)
				{
					CollisionGeometry geometry;

					if (_collisionGeometry.TryGetValue(block.BlockDefinition.Id, out geometry))
					{
						Vector3D world = grid_A.GridIntegerToWorld(point_A);
						Matrix blockOrient;
						block.Orientation.GetMatrix(out blockOrient);
						Quaternion quat = Quaternion.CreateFromRotationMatrix(grid_A.WorldMatrix);
						Matrix.Transform(ref blockOrient, ref quat, out blockOrient);
						MatrixD blockMatrix = new MatrixD(blockOrient);
						blockMatrix.Translation = world;

						Draw(geometry, blockMatrix);
					}
				}
			}
			if (grid_B != null && point_B != null) Utilities.HighlightCell(grid_B, point_B, Color.Red);

			MyConsole.Render(font);

			Common.Call_Add_Billboards(); // just for sure
		}

		private void Draw(CollisionGeometry geometry, MatrixD blockMatrix)
		{
			var material = MyStringId.GetOrCompute("Square");
			foreach (var shape in geometry.Shapes)
			{
				var matrix = blockMatrix * shape.Transform;

				var box = shape as BoxShape;
				if (box != null)
				{	Color color = Color.Cyan;
					
					var bb = new BoundingBoxD(-box.HalfExtents, box.HalfExtents);
					
					MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
						MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);
				}

				var capsule = shape as CapsuleShape;
				if(capsule != null)
				{	Color color = Color.Magenta;

					Vector3D v = new Vector3D(capsule.Radius);

					var bb = new BoundingBoxD(-v+capsule.VertexA, v+capsule.VertexA);

					MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
						MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);

					bb = new BoundingBoxD(-v+capsule.VertexB, v+capsule.VertexB);

					MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
						MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);
				}

				var convex = shape as ConvexHullShape;
				if (convex != null)
				{	Color color = Color.Red;
					
					Vector3D v = new Vector3D(0.01);

					foreach(var vertex in convex.Vertices)
					{	
						var bb = new BoundingBoxD(-v+vertex, v+vertex);

						MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
							MySimpleObjectRasterizer.Wireframe, 1, 0.05f, material, material);
					}
				}

				var sphere = shape as SphereShape;
				if (sphere != null)
				{	Color color = Color.White;

					Vector3D v = new Vector3D(sphere.Radius);

					var bb = new BoundingBoxD(-v, v);

					MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
						MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);
				}

				var cylinder = shape as CylinderShape;
				if (cylinder != null)
				{	Color color = Color.Yellow;

					Vector3D v = new Vector3D(cylinder.Radius);

					var bb = new BoundingBoxD(-v, v);

					MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref bb, ref color,
						MySimpleObjectRasterizer.Wireframe, 1, 0.01f, material, material);
				}
			}
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
