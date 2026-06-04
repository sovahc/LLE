using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LLE
{
	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	public static class Debug
	{
		public static IMyCubeGrid grid;
		public static List<Vector3I> highlightCellsRed = new List<Vector3I>();
		public static List<Vector3I> highlightCellsGreen = new List<Vector3I>();

		internal static void Start(IMyCubeGrid grid_)
		{	grid = grid_;
			highlightCellsRed.Clear();
			highlightCellsGreen.Clear();
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;

		private IMyCubeGrid selectedGrid;
		private Vector3I selectedBlock;
		private Commands commands;

		private readonly StringBuilder llmReasoning = new StringBuilder();
		private readonly StringBuilder llmContent = new StringBuilder();
		private readonly StringBuilder commandToProcess = new StringBuilder();
		private readonly StringBuilder toLLM = new StringBuilder();

		private bool pauseLLM;

		private static Vector3D testSphereCenter;

		public static void Log(string s) => Utilities.Log(s);

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
			//MyAPIGateway.Entities.GetEntities(entities);
			//foreach (var e in entities) OnEntityAdd(e);

			//MyEntities.OnEntityAdd += OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered += OnChatMessage;
		}

		protected override void UnloadData()
		{
			//MyEntities.OnEntityAdd -= OnEntityAdd;
			MyAPIGateway.Utilities.MessageEntered -= OnChatMessage;
		}

		private void CommandResult(string result)
		{
			Log($"CommandResult:\n{result}");
				
			toLLM.Append("[RESULT]:\n");
			toLLM.Append(result);
			toLLM.Append('\n');

			MyConsole.AddMultiline(toLLM.ToString(), Color.GreenYellow);
		}

		MessageType lastType = MessageType.Stop;

		public override void UpdateBeforeSimulation()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			// Lazy initialization
			if (commands == null)
			{
				commands = new Commands(ch);
				LLE_Loader.SetHelp(commands.Help());
			}

			// Process command result
			string result = commands.Update();
			if (result != null)
			{
				CommandResult(result);
				return;
			}

			// Wait for in-progress command to finish
			if (commands.InProgress()) return;

			// Send accumulated results to LLM
			if (toLLM.Length != 0 && !pauseLLM)
			{
				LLE_Loader.SendMessageToLLM(toLLM.ToString());
				toLLM.Clear();
				return;
			}

			// Poll for new chunks from LLM

			for (int i = 0; i < 10; ++i)
			{
				FromLLM m;
				if (!LLE_Loader.GetChunkFromLLM(out m)) return;
				
				// Type changed — log and clear the old buffer
				if (m.Type != lastType)
				{
					switch(lastType)
					{	case MessageType.Reasoning:
							Log($"llmReasoning:\n{llmReasoning}");
							llmReasoning.Clear();
							break;
						case MessageType.Content:
							commandToProcess.Append(llmContent);
							commandToProcess.Append("\n");

							Log($"llmContent:\n{llmContent}");
							llmContent.Clear();
							break;
					}

				}
				lastType = m.Type;

				if (m.Type == MessageType.Reasoning)
				{
					MyConsole.AddMultiline(m.Payload, Color.LightGray);
					llmReasoning.Append(m.Payload);
				}
				else if (m.Type == MessageType.Content)
				{
					MyConsole.AddMultiline(m.Payload, Color.Cyan);
					llmContent.Append(m.Payload);
				}
				else if (m.Type == MessageType.Stop)
				{	// LLM stopped sending — try to process accumulated content
					ProcessLlmContent(commandToProcess.ToString());
					commandToProcess.Clear();
					return;
				}
			}
		}

		private void ProcessLlmContent(string content)
		{
			const string error = "No command to execute, a command in backticks is required";

			int lastBacktick = content.LastIndexOf('`');
			if (lastBacktick < 0)
			{	CommandResult(error);
				return;
			}
			int secondLastBacktick = content.LastIndexOf('`', lastBacktick - 1);
			if (secondLastBacktick < 0)
			{	CommandResult(error);
				return;
			}

			string command = content.Substring(secondLastBacktick + 1, lastBacktick - secondLastBacktick - 1);

			toLLM.Append($"`{command}`"); // send back to establish pattern

			if(command == "pause")
			{	pauseLLM = true;
				return;
			}

			string result = commands.Execute(command);
			if (result != null)
				CommandResult(result);
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
			{	Vector3I unused;
				Utilities.MyRaycast(Utilities.GetEngineerCenter(ch), ch.WorldMatrix.Forward,
					out selectedGrid, out selectedBlock, out unused);
			}

			if (selectedGrid != null)
			{	var block = selectedGrid.GetCubeBlock(selectedBlock);
				
				if (block != null)
				{	
					if(MyAPIGateway.Input.IsNewLeftMousePressed())
						MyConsole.Add($"{Commands.Name(block)} at {Formatter.IJK(selectedBlock)}");

					Collisions.Draw(selectedGrid, block);
					Collisions.DrawTraversability(selectedGrid, block);
				}
			}

			// Right click — test sphere collision with block
			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				var engineerCenter = Utilities.GetEngineerCenter(ch);
				testSphereCenter = engineerCenter + 5.0 * ch.WorldMatrix.Forward;
			}

			// Draw test sphere
			if (selectedGrid != null)
			{
				var block = selectedGrid.GetCubeBlock(selectedBlock);
				
				
				bool intersection = false;
				if(block != null)
					intersection = Collisions.CheckWorldSphere(selectedGrid, block, testSphereCenter, 0.5);

				var color = intersection ? new Vector4(1f, 0f, 0f, 1f) : new Vector4(0f, 1f, 0f, 1f);
				Drawing.ScreenSphere(testSphereCenter, 0.5f, color);
				Drawing.RoundMarker(testSphereCenter, intersection ? Color.Red : Color.Lime);
			}

			if(Debug.grid != null)
			{	foreach(var cell in Debug.highlightCellsRed)
					Utilities.HighlightCell(Debug.grid, cell, Color.Red);
				foreach(var cell in Debug.highlightCellsGreen)
					Utilities.HighlightCell(Debug.grid, cell, Color.Green);
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

			MyConsole.Add(message, Color.Chocolate);
			if(message.StartsWith(">"))
			{	message = message.Substring(1);
				pauseLLM = true;
				commands.Execute(message);
			}
			else
			{	if(message == "go") pauseLLM = false;
				
				LLE_Loader.SendMessageToLLM($"[GAME CHAT] {player.DisplayName}: {message}");
			}
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;
		public static bool GetChunkFromLLM(out FromLLM m) { m = null; return false; }
		public static void SendMessageToLLM(string text) { }
		public static void SetHelp(string text) { }
	}
}
