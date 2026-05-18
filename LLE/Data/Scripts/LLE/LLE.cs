using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
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

			//Drawing.RoundMarker(world, color);
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
		private List<Vector3I> debug = new List<Vector3I>();
		private List<Vector3D> debug2 = new List<Vector3D>();

		private IMyCubeGrid grid;
		private IEnumerator iterator;

		private BitField blockedX, blockedY, blockedZ;

		private const double OA = 1.25, OB = 1.10;

		private readonly Vector2D[] ScanOffsets = {
			// slightly rotated
			new Vector2D(-OA, -OB),
			new Vector2D(+OB, -OA),
			new Vector2D(+OA, +OB),
			new Vector2D(-OB, +OA),
		};

		public void DrawDebug()
		{	foreach(var v in debug)
				Utilities.HighlightCell(grid, v, Color.Magenta);
			var material = MyStringId.GetOrCompute("Square");
			var color = Color.Red.ToVector4();
			for(int i = 0; i < debug2.Count; i+=2)
			{	//Drawing.RoundMarker(debug2[i], Color.Red);
				//Drawing.RoundMarker(debug2[i+1], Color.Blue);

				MySimpleObjectDraw.DrawLine(debug2[i], debug2[i+1], material, ref color, 0.01f);
			}
		}

        public void SetGrid(IMyCubeGrid g)
		{	if(g == grid) return;
			// clear cache
			grid = g;
			Vector3I gridSize = grid.Max - grid.Min + 1;

			Utilities.Log($"SetGrid gridSize {gridSize}");
		}

		public void Iteration()
		{	if(iterator == null)
				iterator = Iterator();

			var stopAfter = Stopwatch.GetTimestamp() + TimeSpan.TicksPerMillisecond / 2;

			for(int i = 0; i < 100; ++i)
			{	if(!iterator.MoveNext())
				{	iterator = null;
					return;
				}
				if(Stopwatch.GetTimestamp() >= stopAfter) break;
			}
		}

		private IEnumerator Iterator()
		{	debug.Clear();
			debug2.Clear();
			
			if(grid == null) yield break;

			MyCubeGrid g = grid as MyCubeGrid;
			if(g == null) yield break;

			MyDefinitionId fullArmor = new MyDefinitionId(typeof(MyObjectBuilder_CubeBlock), "LargeBlockArmorBlock");

			var Min = grid.Min;
			var Max = grid.Max;

			Indexer indexer = new Indexer(Max - Min + 2);
			blockedX = new BitField(indexer.Count, 1);
			blockedY = new BitField(indexer.Count, 1);
			blockedZ = new BitField(indexer.Count, 1);

			Vector3I v = new Vector3I();
			Vector3I end = new Vector3I();
			Vector3I unused = new Vector3I();
			var zero = grid.GridIntegerToWorld(v);
			var unitX = grid.GridIntegerToWorld(v + Vector3I.UnitX) - zero;
			unitX.Normalize();
			//var unitY = grid.GridIntegerToWorld(v + Vector3I.UnitY) - zero;
			//var unitZ = grid.GridIntegerToWorld(v + Vector3I.UnitZ) - zero;

			LineD line = new LineD();

			const double CubeSize = 5;

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
						{	blockedX.Set(index, 1);
							blockedX.Set(index+1, 1); // полный блок блокирует сразу два портала
							continue;
						}

						if(blockedX.Get(index) != 0) continue; // текуущий портал блокирован предыдущим блоком

						// тестируем проходимость лучами

						line.From = grid.GridIntegerToWorld(v);
						line.To = grid.GridIntegerToWorld(end);

						double minimalSq = double.MaxValue;

						//for(int o = 0; o < ScanOffsets.Length; ++o)
						//{	
							//line.From.Z += ScanOffsets[o].X;
							//line.From.Y += ScanOffsets[o].Y;
							//line.To.Z += ScanOffsets[o].X;
							//line.To.Y += ScanOffsets[o].Y;

							double dist = (line.To-line.From).Length();
							double dsq = 10000;

							if(grid.GetLineIntersectionExactGrid(ref line, ref unused, ref dsq))
								minimalSq = dsq;

							//if(dsq < minimalSq) minimalSq = dsq;

							
						//}

						double minimal = Math.Sqrt(minimalSq);

						debug2.Add(grid.GridIntegerToWorld(v));

						while(minimal > CubeSize && v.Z <= Max.Z)
						{	minimal -= CubeSize;
							++v.Z;							
						}
						//debug.Add(line.From+unitX*minimal);
						debug2.Add(grid.GridIntegerToWorld(v));

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
			{	IMyCubeGrid grid;
				Vector3I position;
				Utilities.MyRaycast(pm.Translation, pm.Forward, out grid, out position, 250);

				if(grid != null) trav.SetGrid(grid);
			}
			//if (MyAPIGateway.Input.IsNewRightMousePressed())
			Profiler p = new Profiler();
			p.Start();
			trav.Iteration();
			p.Stop();
			//MyConsole.Clear();
			MyConsole.Add($"{p}", Color.IndianRed);
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

			trav.DrawDebug();

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
