using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.ModAPI;
using VRage.Utils;
using Sandbox.ModAPI;

namespace LLE
{
	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	public static class Debug
	{
		public static IMyCubeGrid grid;
		
		public static Vector3I? astarStart;
		public static Vector3I? astarGoal;

		private static AStarHelper aStarHelper;

		private static List<LineD> lines = new List<LineD>();
		private static List<Color> lineColors = new List<Color>();

		public static void ClearLines()
		{	lines.Clear();
			lineColors.Clear();			
		}

		public static void AddLine(LineD line, Color color)
		{	lines.Add(line);
			lineColors.Add(color);
		}

		internal static void Start(IMyCubeGrid grid_)
		{	grid = grid_;

			astarStart = null;
			astarGoal = null;
		}

		internal static void Pathfinding(MatrixD hm)
		{
			if (grid == null) return;

			Vector3D ahead = hm.Translation + hm.Forward * 5;

			var cell = grid.WorldToGridInteger(ahead);

			Utilities.HighlightCell(grid, cell, Color.Green);

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

		internal static void Draw(MatrixD hm)
		{
			var material = MyStringId.GetOrCompute("Square");

			if (grid != null && astarStart != null)
			{
				var block = grid.GetCubeBlock(astarStart.Value);
				if(block != null)
				{	List<EQSResult> results = new List<EQSResult>();
					EQS.Query(block, hm.Translation, InteractionKind.Inventory, results, 5);
				}
			}

			if (grid == null) return;

			if(astarStart != null) Drawing.RoundMarker(grid.GridIntegerToWorld(astarStart.Value), Color.Green);
			if(astarGoal != null) Drawing.RoundMarker(grid.GridIntegerToWorld(astarGoal.Value), Color.Red);

			if(astarStart != null)
			{	var block = grid.GetCubeBlock(astarStart.Value);
				if(block != null)
				{
					Collisions.Draw(block);
					//Collisions.DrawTraversability(grid, astarStart.Value);
				}
			}

			for(int i = 0; i < lines.Count; ++i)
			{
				var line = lines[i];
				var color = lineColors[i].ToVector4();
				MySimpleObjectDraw.DrawLine(line.From, line.To, material, ref color, 0.01f);
				Drawing.RoundMarker(line.To, lineColors[i]);
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

		private IMyControl toggle_signals;		

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

			MyAPIGateway.Entities.OnEntityAdd += OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered += OnChatMessage;
		}

		protected override void UnloadData()
		{
			if (toggle_signals != null) toggle_signals.IsEnabled = true;

			MyAPIGateway.Entities.OnEntityAdd -= OnEntityAdd;
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
				EQS.Initialize();
				commands = new Commands(ch);
				llm = new LLM(commands);
			}

			llm.Tick();
		}

		// HACK: Intercept Shift+H by temporarily disabling 'H'
		private void HandleConsoleToggle()
		{
			if (toggle_signals == null)
				toggle_signals = MyAPIGateway.Input.GetGameControl(MyStringId.GetOrCompute("TOGGLE_SIGNALS"));
			if (toggle_signals == null) return;

			if (MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible)
			{
				toggle_signals.IsEnabled = true;
				return;
			}

			if (MyAPIGateway.Input.IsKeyPress(MyKeys.Shift))
			{
				toggle_signals.IsEnabled = false;

				if (MyAPIGateway.Input.IsNewKeyPressed(MyKeys.H)) MyConsole.Visible = !MyConsole.Visible;
			}
			else
			{	toggle_signals.IsEnabled = true;
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
				Debug.Draw(hm);
			}

			MyConsole.Render(font);
			Common.Call_Add_Billboards(); // just to be sure
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
			else if(message == ">sm")
			{	var result = commands.Execute("select Miner");
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
