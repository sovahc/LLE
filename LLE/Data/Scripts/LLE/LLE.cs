using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.Game.EntityComponents;

namespace LLE
{
	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	public static class Debug
	{
		public static bool Vision = false;

		public static IMyCubeGrid grid;
		
		public static Vector3I? astarStart;
		public static Vector3I? astarGoal;

		private static AStarHelper aStarHelper;

		public static List<LineD> linesRed = new List<LineD>();
		public static List<LineD> linesGray = new List<LineD>();

		internal static void Start(IMyCubeGrid grid_)
		{	grid = grid_;

			astarStart = null;
			astarGoal = null;
		}

		internal static void Pathfinding(MatrixD hm)
		{
			if (grid == null) return;

			Vector3D ahead = hm.Translation + hm.Forward * 7;

			var cell = grid.WorldToGridInteger(ahead);

			Utilities.HighlightCell(grid, cell, Color.Gray);

			var lm = MyAPIGateway.Input.IsNewLeftMousePressed();
			var rm = MyAPIGateway.Input.IsNewRightMousePressed();

			if (lm)
			{	astarStart = cell;
				MyConsole.Add($"A* start: {cell}", Color.Green);
			}
			if (rm)
			{	astarGoal = cell;
				MyConsole.Add($"A* goal: {cell}", Color.Red);
			}

			if (lm || rm)
			{
				if(astarStart != null && astarGoal != null)
				{
					aStarHelper = new AStarHelper(grid, astarStart.Value, astarGoal.Value);
				}
			}

			if(aStarHelper != null)
			{	aStarHelper.Tick();
				aStarHelper.DrawPath();
			}
		}

		internal static void Draw(MatrixD hm, Commands commands)
		{
			if(grid == null) return;

			if(astarStart != null) Drawing.RoundMarker(grid.GridIntegerToWorld(astarStart.Value), Color.Green);
			if(astarGoal != null) Drawing.RoundMarker(grid.GridIntegerToWorld(astarGoal.Value), Color.Red);

			if(astarStart != null)
			{	var block = grid.GetCubeBlock(astarStart.Value);
				if(block != null)
				{
					Collisions.Draw(block);
					Collisions.DrawTraversability(grid, astarStart.Value);
				}
			}

			var material = MyStringId.GetOrCompute("Square");
			var red = Color.Red.ToVector4();
			var gray = Color.Gray.ToVector4();
					
			foreach(var line in linesGray)
			{	MySimpleObjectDraw.DrawLine(line.From, line.To, material, ref gray, 0.01f);
			}
			foreach(var line in linesRed)
			{	MySimpleObjectDraw.DrawLine(line.From, line.To, material, ref red, 0.01f);
				Drawing.RoundMarker(line.To, Color.OrangeRed);
			}
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;

		private Commands commands;

		private bool initialized;
		private LLM llm;

		private IMyControl signalsControl;		

		public static void Log(string s) { MyLog.Default.WriteLine("LLE " + s); }

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
			if (signalsControl != null) signalsControl.IsEnabled = true;

			MyEntities.OnEntityAdd -= OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered -= OnChatMessage;
		}

		public override void UpdateBeforeSimulation()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			HandleConsoleToggle();

			// Lazy initialization

			if (!initialized)
			{
				initialized = true;

				LLE_Loader.SetHelp(Commands.Help());
				Vision.Initialize();
				commands = new Commands(ch);
				llm = new LLM(commands);
			}

			llm.Tick();
		}

		// HACK: While Shift is held, the game's TOGGLE_SIGNALS control is disabled via IsEnabled=false,
		// so Shift+H belongs to the mod (toggles console) instead of the game (toggles signals).
		// IsNewKeyPressed reads the raw key state and is not affected by IsEnabled, so H is still detected.
		private void HandleConsoleToggle()
		{
			if (signalsControl == null)
				signalsControl = MyAPIGateway.Input.GetGameControl(MyStringId.GetOrCompute("TOGGLE_SIGNALS"));

			if (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible)
			{
				if (signalsControl != null) signalsControl.IsEnabled = true;
				return;
			}

			if (MyAPIGateway.Input.IsKeyPress(MyKeys.Shift))
			{
				if (signalsControl != null) signalsControl.IsEnabled = false;

				if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.H))
					MyConsole.Visible = !MyConsole.Visible;
			}
			else
			{
				if (signalsControl != null) signalsControl.IsEnabled = true;
			}
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			Common.StartFrame();

			if(!LLE_Loader.IsPresent())
			{	font.String("LLE_Loader is not present.", new Vector2D(0.0, -0.1), 0.00075f, Color.Red);
				Common.Call_Add_Billboards();
				return;
			}

			if(initialized)
			{
				var hm = ch.GetHeadMatrix(false, false);
				Debug.Pathfinding(hm);
				Debug.Draw(hm, commands);
			}

			MyConsole.Render(font);
			Common.Call_Add_Billboards(); // just for sure
		}

		public static void DrawOctNode(AsteroidNavigation nav, OctreeNode node, bool marker)
		{
			Color color;
			switch(node.Type)
			{	case NodeType.Free: color = Color.Green; break;
				case NodeType.Blocked: color = Color.Red; break;
				default: color = Color.Gray; break;
			}

			var bb = nav.NodeToWorldBB(node);

			MatrixD matrix = MatrixD.Identity;
			matrix.Translation = new Vector3D(
				(bb.Min.X + bb.Max.X) * 0.5,
				(bb.Min.Y + bb.Max.Y) * 0.5,
				(bb.Min.Z + bb.Max.Z) * 0.5);

			if(marker) Drawing.RoundMarker(matrix.Translation, color);

			var half = (bb.Max - bb.Min) * 0.49;
			var localBb = new BoundingBoxD(-half, half);

			var material = MyStringId.GetOrCompute("Square");
			MySimpleObjectDraw.DrawTransparentBox(ref matrix, ref localBb, ref color,
				MySimpleObjectRasterizer.Wireframe, 1, 0.003f, material, material);
		}

		void OnEntityAdd(IMyEntity entity)
		{
			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				grid.OnClose += OnClose;

				grid.OnBlockAdded += Vision.OnBlockAdded;
				grid.OnBlockRemoved += Vision.OnBlockRemoved;
				grid.OnGridChanged += Vision.OnGridChanged;
				grid.OnGridSplit += Vision.OnGridSplit;
				grid.OnGridMerge += Vision.OnGridMerge;
			}
		}

		void OnClose(IMyEntity entity)
		{
			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				Vision.OnClose(grid);

				grid.OnBlockAdded -= Vision.OnBlockAdded;
				grid.OnBlockRemoved -= Vision.OnBlockRemoved;
				grid.OnGridChanged -= Vision.OnGridChanged;
				grid.OnGridSplit -= Vision.OnGridSplit;
				grid.OnGridMerge -= Vision.OnGridMerge;
			}
		}

		void OnChatMessage(string message, ref bool sendToOthers)
		{
			if (!LLE_Loader.IsPresent()) return;
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			if (commands == null) return;

			if(message == ">sp")
			{	var result = commands.Execute("select Platform");
				MyConsole.AddMultiline(result?.Message, Color.SeaGreen);
			}
			else if(message == ">spawn")
			{	var bot = Bot.Spawn(player);
				MyConsole.Add($"bot={bot}");
				if(bot == null) return;				

				commands = new Commands(bot);
				llm = new LLM(commands);
			}
			else if(message.StartsWith(">"))
			{	var command = message.Substring(1).Trim();

				MyConsole.AddMultiline(">", Color.Red);
				MyConsole.AddMultiline(command, Color.Magenta);
				MyConsole.AddMultiline("\n", Color.Magenta);
				
				var result = commands.Execute(command);
				MyConsole.AddMultiline(result?.Message, Color.SeaGreen);
				Log(">" + command + ": " + result?.Message);
			}
			else
			{	llm.Append("[GAME CHAT]", Color.Red);
				llm.Append($" {player.DisplayName}: {message}\n", Color.Magenta);
				LLM.pause = false;
				llm.ResetLoopDetector();
			}
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;
		public static bool GetChunkFromLLM(out FromLLM m) { m = null; return false; }
		public static void SendMessageToLLM(string text) { }
		public static void SetHelp(string text) { }
		public static void GetContextStatus(out int usedChars, out int totalChars) { usedChars = 0; totalChars = 0; }
		public static void RestartContext() { }
	}
}
