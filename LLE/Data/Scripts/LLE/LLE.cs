using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Definitions;
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
	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	class Constants
	{
		public const float EngineerCapsuleHeight = 0.8f;
		public const float EngineerCapsuleRadius = 0.5f;
		public const float EngineerHeight = 1.8f;

		public static readonly Vector3I[] SixDirections = new Vector3I[] {
			new Vector3I(1, 0, 0), new Vector3I(-1, 0, 0),
			new Vector3I(0, 1, 0), new Vector3I(0, -1, 0),
			new Vector3I(0, 0, 1), new Vector3I(0, 0, -1)};
	}

	class Utilities
	{
		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

		public static string GetNextWord(ref string s)
		{
			int spaceIndex = s.IndexOf(' ');
			string word = spaceIndex >= 0 ? s.Substring(0, spaceIndex) : s;
			s = spaceIndex >= 0 ? s.Substring(spaceIndex + 1) : "";
			return word;
		}

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
			float blockSize = MyDefinitionManager.Static.GetCubeSize(grid.GridSizeEnum);

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

		public static Vector3D GetEngineerCenter(IMyCharacter ch)
		{
			return ch.GetPosition() + Constants.EngineerHeight/2 * ch.WorldMatrix.Up;
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

		public static void Clear()
		{
			_lines.Clear();
		}

		public static void Add(string text)
		{
			Add(text, Color.White);
		}

		public static void Add(string text, Color color)
		{
			Utilities.Log(text);
			_lines.Add(new LineData { Text = text, Color = color });
			while (_lines.Count > MaxLines) _lines.RemoveAt(0);
		}

		public static void AddMultiline(string text)
		{	if(text == null) return;

			foreach(var line in text.Split('\n'))
			{	if(line.StartsWith("##")) Add(line, Color.Blue);
				else if(line.StartsWith("#")) Add(line, Color.BlueViolet);
				else if(line.StartsWith("*")) Add(line, Color.Gray);
				else Add(line);
			}
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

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;

		IMyCubeGrid selectedGrid;
		Vector3I selectedBlock, selectedFreeSpace;
		bool grind;

		BotTools botTools = new BotTools();

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

			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			MacroNavigation.Update(ch);

			ServerCommand cmd;
			if (LLE_Loader.GetCommand(out cmd))
			{
				var p = cmd.Payload.Trim();

				var command = Utilities.GetNextWord(ref p);
				command = command.ToUpperInvariant();
				var arguments = p; p = null;

				MyConsole.Add($"command: {command} arguments: '{arguments}'", Color.Cyan);

				var engineer = Utilities.GetEngineerCenter(ch);
				string message;

				if(command == "HELP")
				{	Commands.Help(out message);
				}
				else if(command == "SELECT_ASTEROID")
				{	Commands.Select(ObjectType.Asteroid, engineer, arguments, out message);
				}
				else if(command == "SELECT_GRID" || command == "SELECT")
				{	Commands.Select(ObjectType.LargeShip, engineer, arguments, out message);
				}
				else if(command == "FLY")
				{	Commands.Fly(ch, arguments, out message);
				}
				else if(command == "GRIND")
				{	if(selectedGrid != null) grind = true;
					message = "Grind";
				}
				else if(command == "TEST")
				{	if(selectedGrid != null)
					{	var block = selectedGrid.GetCubeBlock(selectedBlock);
						
						message = "";
						Dictionary<string, int> comp = new Dictionary<string, int>();
						BotTools.GetStockpileComponents(block, comp);

						foreach(var kv in comp) message += $"E {kv.Key} {kv.Value}\n";
					}
					else
					{	message  = "error";
					}
				}
				else
				{	message = $"Unknown command '{command}' use `help` to list all avialable commands.";
				}
				MyConsole.AddMultiline(message);
			}

			if(grind)
			{	if(!botTools.GrindBlock(ch, selectedGrid.GetCubeBlock(selectedBlock)))
				{	grind = false;
					botTools.Stop();
					MyConsole.Add("Stop");
					//message = $"result {r}";
				}
			}

			//var pm = ch.GetHeadMatrix(false);
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			var pm = ch.GetHeadMatrix(false);

			Common.StartFrame();

			if(!LLE_Loader.IsPresent())
			{	font.String("No LLE_Loader is present.", new Vector2D(0.0, -0.1), 0.00075f, Color.Red);
				Common.Call_Add_Billboards();
				return;
			}

			if(MyAPIGateway.Input.IsNewLeftMousePressed())
			{	Utilities.MyRaycast(Utilities.GetEngineerCenter(ch), ch.WorldMatrix.Forward,
					out selectedGrid, out selectedBlock, out selectedFreeSpace);
				MyConsole.Add($"selectedFreeSpace {selectedFreeSpace}", Color.DarkSalmon);
			}

			if (selectedGrid != null)
			{	var block = selectedGrid.GetCubeBlock(selectedBlock);
				
				if (block != null)
				{	Collisions.Draw(selectedGrid, block);
					//DrawTraversability(block);
				}
				Utilities.HighlightCell(selectedGrid, selectedFreeSpace, Color.GreenYellow);
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

				grid.OnBlockAdded += Grid_OnBlockAdded;
				grid.OnBlockRemoved += Grid_OnBlockRemoved;
				grid.OnGridChanged += Grid_OnGridChanged;
			}
		}

		void OnClose(IMyEntity entity)
		{
			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnBlockAdded -= Grid_OnBlockAdded;
				grid.OnBlockRemoved -= Grid_OnBlockRemoved;
				grid.OnGridChanged -= Grid_OnGridChanged;
			}
		}

		public static void Grid_OnBlockAdded(IMySlimBlock block) { }

		public static void Grid_OnBlockRemoved(IMySlimBlock block) { }

		public static void Grid_OnGridChanged(IMyCubeGrid grid) { }

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
