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

		public static IMyCubeGrid grid;
		public static Vector3I? astarStart;
		public static Vector3I? astarGoal;
		private static AStarHelper ash;

		internal static void Start(IMyCubeGrid grid_)
		{	grid = grid_;
			astarStart = null;
			astarGoal = null;
		}

		internal static void Pathfinding(MatrixD hm)
		{
			if (grid == null) return;

			Vector3D ahead = hm.Translation + hm.Forward * 7.5;

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
					ash = new AStarHelper(grid, astarStart.Value, astarGoal.Value);
				}
			}

			var material = MyStringId.GetOrCompute("Square");

			if (grid != null && astarStart != null)
			{
				var block = grid.GetCubeBlock(astarStart.Value);
				if(block != null)
				{	
					Collisions.Draw(block);
					Collisions.DrawTraversability(grid, astarStart.Value);
					
					List<EQSResult> results = new List<EQSResult>();
					EQS.Query(block, hm.Translation, InteractionKind.Inventory, results, 5);
				}
			}

			if(ash != null)
			{	ash.Tick();

				var path = ash.GetPath();

				foreach(var p in path)
					Drawing.RoundMarker(ash.CellToWorld(p), Color.Yellow);
			}

			if(astarStart != null) Drawing.RoundMarker(grid.GridIntegerToWorld(astarStart.Value), Color.Green);
			if(astarGoal != null) Drawing.RoundMarker(grid.GridIntegerToWorld(astarGoal.Value), Color.Red);

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

			if (!initialized)
			{
				initialized = true;

				commands = new Commands(ch);
				commands.SetSystemPromptAndMemory();

				Vision.Initialize();
				EQS.Initialize();
				
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
				if(MyConsole.Visible)
				{	var hm = ch.GetHeadMatrix(false, false);
					Debug.Pathfinding(hm);
				}

				ScreenshotOverlay.Render(font);
			}

			MyConsole.Render(font);
			Common.Call_Add_Billboards();
		}

		void OnEntityAdd(IMyEntity entity)
		{
			var grid = entity as IMyCubeGrid;
			if (grid != null)
			{
				if (grid.DisplayName == Commands.DraftGridName) return;

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

			if(message == ">spawn")
			{	var bot = Bot.Spawn(player);
				MyConsole.Add($"bot={bot}");
				if(bot == null) return;

				commands = new Commands(bot);
				llm = new LLM(commands);
			}
			else if(message == ">sp")
			{	string error;
				var call = ToolCall.Parse("select", "{\"name\":\"Red Platform\"}", out error);
				if(call == null)
				{	MyConsole.Add($"ToolCall.Parse: {error}", Color.Red);
					return;
				}

				MyConsole.Add(call.Text, Color.Yellow);
				var result = commands.Execute(call);
				MyConsole.Add(result == null ? "(running)" : result.Message, Color.Yellow);
			}
			else if(message.StartsWith(">"))
			{	MyConsole.Add("Commands from chat are gone — the model calls tools directly.", Color.Red);
			}
			else
			{	llm.Append("[GAME CHAT]", Color.Red);
				llm.Append($" {player.DisplayName}: {message}\n", Color.Magenta);
				LLM.pause = false;
				llm.ResetLoopDetector();
			}
		}
	}

	// Every body here is replaced by a Harmony prefix in LLELoader; none of them ever runs.
	public static class LLE_Loader
	{
		public static bool IsPresent() => false;
		public static bool GetChunkFromLLM(int channel, out FromLLM m) { m = null; return false; }
		public static void SendMessageToLLM(int channel, string requestJson) { }
		public static void CancelLLM(int channel) { }
		public static string TakeScreenshot() { return null; }
		public static int GetContextChars(int channel) { return 0; }
		public static void RequestScreenshot() { }
		public static bool ScreenshotDone(out bool success) { success = false; return true; }
	}
}
