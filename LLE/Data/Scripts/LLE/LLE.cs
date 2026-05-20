using System;
using System.Collections;
using System.IO;
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
using VRage.Game.Models;
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

	class Traversability
	{
		private List<Vector3D> debug1 = new List<Vector3D>();
		private List<Vector3D> debug2 = new List<Vector3D>();
		private List<Vector3D> debug4 = new List<Vector3D>();

		private IMyCubeGrid grid;
		private IEnumerator iterator;

		private Indexer indexer;
		private BitField blockedX, blockedY, blockedZ;

		private const double OA = 1.25/5, OB = 1.10/5;

		private readonly Vector2D[] ScanOffsets = {
			// slightly rotated
			new Vector2D(-OA, -OB),
			new Vector2D(+OB, -OA),
			new Vector2D(+OA, +OB),
			new Vector2D(-OB, +OA),
		};

		public void DrawDebug()
		{	for(int i = 0; i < debug1.Count; ++i)
			{	Drawing.RoundMarker(debug1[i], Color.Red);
			}
			var material = MyStringId.GetOrCompute("Square");
			var color = Color.Magenta.ToVector4();
			for(int i = 0; i < debug2.Count; i += 2)
			{	
				MySimpleObjectDraw.DrawLine(debug2[i], debug2[i+1], material, ref color, 0.01f);
				Drawing.RoundMarker(debug2[i], Color.Gray);
				Drawing.RoundMarker(debug2[i+1], Color.Red);
			}

			color.W = 0.1f;
			MyQuadD d = new MyQuadD();
			for(int i = 0; i < debug4.Count; i += 4)
			{	
				d.Point0 = debug4[i+0];
				d.Point1 = debug4[i+1];
				d.Point2 = debug4[i+2];
				d.Point3 = debug4[i+3];
				Common.Billboard(d, material, color);
			}
		}

        public void SetGrid(IMyCubeGrid g)
		{	if(g == grid) return;
			// clear cache
			grid = g;
			Vector3I gridSize = grid.Max - grid.Min + 1;
			indexer = new Indexer(grid.Max - grid.Min + 2);

			Utilities.Log($"SetGrid gridSize {gridSize}");

			iterator = Iterator(); // run calculation
		}

		public void Iteration() => Utilities.Tick(ref iterator, "trav");

		private void SetBlockedZ(Vector3I v)
		{	var index = indexer.Index(v - grid.Min);
			blockedZ.Set(index, 1);

			var zero = grid.GridIntegerToWorld(v);
			var halfCubeX = (grid.GridIntegerToWorld(v + Vector3I.UnitX) - zero) * 0.5;
			var halfCubeY = (grid.GridIntegerToWorld(v + Vector3I.UnitY) - zero) * 0.5;
			var halfCubeZ = (grid.GridIntegerToWorld(v + Vector3I.UnitZ) - zero) * 0.5;

			zero -= halfCubeZ * 1.05;

			debug4.Add(zero - halfCubeX - halfCubeY);
			debug4.Add(zero + halfCubeX - halfCubeY);
			debug4.Add(zero + halfCubeX + halfCubeY);
			debug4.Add(zero - halfCubeX + halfCubeY);
		}

		private byte GetBlockedZ(Vector3I v)
		{	var index = indexer.Index(v - grid.Min);
			return blockedZ.Get(index);
		}

		private IEnumerator Iterator()
		{	debug1.Clear();
			debug2.Clear();
			debug4.Clear();
			
			if(grid == null) yield break;

			MyCubeGrid g = grid as MyCubeGrid;
			if(g == null) yield break;

			MyDefinitionId fullArmor = new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorBlock");

			var Min = grid.Min;
			var Max = grid.Max;
			
			//blockedX = new BitField(indexer.Count, 1);
			//blockedY = new BitField(indexer.Count, 1);
			blockedZ = new BitField(indexer.Count, 1);

			Vector3I v = new Vector3I();
			Vector3I end = new Vector3I();
			Vector3I unused = new Vector3I();
			var zero = grid.GridIntegerToWorld(Vector3I.Zero);
			var cubeX = grid.GridIntegerToWorld(Vector3I.UnitX) - zero;
			var cubeY = grid.GridIntegerToWorld(Vector3I.UnitY) - zero;
			var cubeZ = grid.GridIntegerToWorld(Vector3I.UnitZ) - zero;

			double cubeSize = cubeZ.Length();

			LineD line = new LineD();
			LineD line2 = new LineD();

			for(v.X = Min.X; v.X <= Max.X; ++v.X)
			{	for(v.Y = Min.Y; v.Y <= Max.Y; ++v.Y)
				{	
					end.X = v.X;
					end.Y = v.Y;
					end.Z = Max.Z;
					
					for(v.Z = Min.Z; v.Z <= Max.Z; ++v.Z)
					{	var block = grid.GetCubeBlock(v);
						
						if(block == null) continue;

						// 1 0 1  // blocks
						//| | | | // sides (portals)

						var index = indexer.Index(v - Min);

						MyDefinitionId def = block.BlockDefinition.Id;
						if(def == fullArmor)
						{	SetBlockedZ(v);
							SetBlockedZ(v+Vector3I.UnitZ); // полный блок блокирует сразу два портала
							continue;
						}

						if(GetBlockedZ(v) != 0) continue; // текущий портал блокирован предыдущим блоком ^

						// тестируем проходимость портала лучами

						var zShift = cubeZ * 0.75f;

						line.From = grid.GridIntegerToWorld(v) - zShift;
						line.To = grid.GridIntegerToWorld(end) + zShift;
						
						var vector = line.To-line.From;
						double lineLength = vector.Length();

						double minimalIntersection = 1e10;

						for(int o = 0; o < ScanOffsets.Length; ++o)
						{	
							var Xoff = ScanOffsets[o].X * cubeX;
							var Yoff = ScanOffsets[o].Y * cubeY;

							line2.From = line.From + Xoff + Yoff;
							line2.To = line.To + Xoff + Yoff;

							double dsq = lineLength*lineLength;
							var intersection = line2.To;

							grid.GetLineIntersectionExactGrid(ref line2, ref unused, ref dsq);
							
							double d = Math.Sqrt(dsq);
							intersection = line2.From + vector * d / lineLength;

							if(d < minimalIntersection)
								minimalIntersection = d;

							debug2.Add(line2.From);
							debug2.Add(intersection);
						}

						// каждый следующий портал через который прошли лучи считается проходимым

						int steps = (int)Math.Floor(minimalIntersection / cubeSize);
						if(steps < 0) throw new Exception("steps < 0");

						v.Z += steps;
						if(v.Z <= Max.Z) SetBlockedZ(v);

						yield return null;
					}
				}
			}
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;

		Traversability trav = new Traversability();

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

		IMyCubeGrid grid_A, grid_B;
		Vector3I point_A, point_B;
		AStar astar;
		const int border = 1;

		HashSet<MyDefinitionId> tested = new HashSet<MyDefinitionId>();

		void testBlock(IMyEntity block, string fileName)
		{
			BoundingSphere sphere = new BoundingSphere(Vector3D.Zero, 100.0f);
			var triangles = new List<MyTriangle_Vertex_Normals>();

			Profiler p = new Profiler("GetTrianglesIntersectingSphere");
			block.GetTrianglesIntersectingSphere(ref sphere, null, null, triangles, int.MaxValue);
			p.Stop();

			MyConsole.Add($"Block: {triangles.Count} triangles {p}", Color.YellowGreen);
			SaveStl(fileName + ".stl", triangles);
		}

		static void SaveStl(string fileName, List<MyTriangle_Vertex_Normals> triangles)
		{
			using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(fileName, typeof(LLE)))
			{
				if (writer == null) return;
				writer.WriteLine("solid " + fileName);
				foreach (var tri in triangles)
				{
					var v0 = tri.Vertices.Vertex0;
					var v1 = tri.Vertices.Vertex1;
					var v2 = tri.Vertices.Vertex2;

					Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0);
					float len = (float)Math.Sqrt(normal.X*normal.X + normal.Y*normal.Y + normal.Z*normal.Z);
					if(len > 0) normal /= len;

					writer.WriteLine("facet normal {0} {1} {2}", normal.X, normal.Y, normal.Z);
					writer.WriteLine("  outer loop");
					writer.WriteLine("    vertex {0} {1} {2}", v0.X, v0.Y, v0.Z);
					writer.WriteLine("    vertex {0} {1} {2}", v1.X, v1.Y, v1.Z);
					writer.WriteLine("    vertex {0} {1} {2}", v2.X, v2.Y, v2.Z);
					writer.WriteLine("  endloop");
					writer.WriteLine("endfacet");
				}
				writer.WriteLine("endsolid");
				writer.Flush();
			}
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

			bool pointChanged = false;
			if (MyAPIGateway.Input.IsNewLeftMousePressed())
			{
				Utilities.MyRaycast(pm.Translation, pm.Forward, out grid_A, out point_A);
				pointChanged = true;
			}
			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				Utilities.MyRaycast(pm.Translation, pm.Forward, out grid_B, out point_B);
				pointChanged = true;
			}

			if (MyAPIGateway.Input.IsNewMiddleMousePressed())
			{
				IMyCubeGrid dump_grid;
				Vector3I dump_pos;
				Utilities.MyRaycast(pm.Translation, pm.Forward, out dump_grid, out dump_pos);
				if (dump_grid != null)
				{
					var slim = dump_grid.GetCubeBlock(dump_pos);
					if (slim?.FatBlock != null) LLE_Loader.DumpEntity(slim.FatBlock.EntityId);
				}
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
					if(slim.FatBlock != null)
					{
						if(!tested.Contains(slim.BlockDefinition.Id))
						{	tested.Add(slim.BlockDefinition.Id);

							var n = tested.Count;
							testBlock(slim.FatBlock, $"{n}_{slim.BlockDefinition.Id.SubtypeName}");
						}
					}
					
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
			if (grid_B != null && point_B != null) Utilities.HighlightCell(grid_B, point_B, Color.Red);

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
		public static void DumpEntity(long entityId) { }
	}
}
