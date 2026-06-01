using System.Collections.Generic;
using System.Text;
using Sandbox.Game.Entities;
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
		public static List<Vector3I> highlightCells = new List<Vector3I>();
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
		private readonly StringBuilder toLLM = new StringBuilder();

		private MessageType lastMessageType = MessageType.Stop;
        private bool pauseLLM;

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

		public override void UpdateBeforeSimulation()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			if(commands == null)
			{	commands = new Commands(ch);
				LLE_Loader.SetHelp(commands.Help());
			}

			commands.Update(); // updates commandResult

			if (commands.commandResult != null)
			{	
				// collect command results into buffer for sending to LLM

				Log($"commandResult:\n{commands.commandResult}");
				
				MyConsole.AddMultiline("\n", Color.LightGray);
				MyConsole.AddMultiline(commands.commandResult, Color.GreenYellow);
				MyConsole.AddMultiline("\n", Color.LightGray);

				toLLM.Append("[RESULT]:\n");
				toLLM.Append(commands.commandResult);
				toLLM.Append('\n');

				commands.commandResult = null;
			}

			if(commands.InProgress()) return;

			if (lastMessageType == MessageType.Stop)
			{	// LLM is waiting for a response

				if (llmContent.Length != 0)
				{	// command buffer is not empty
					string content = llmContent.ToString();
					int lastBacktick = content.LastIndexOf('`');
					if (lastBacktick > 0)
					{
						int secondLastBacktick = content.LastIndexOf('`', lastBacktick - 1);
						if (secondLastBacktick >= 0)
						{
							string command = content.Substring(secondLastBacktick + 1, lastBacktick - secondLastBacktick - 1);
							llmContent.Clear();
							toLLM.Append($"`{command}`"); // send back to establish pattern
							commands.Execute(command);
							return; // critical
						}
					}
				}
				else if(toLLM.Length != 0 && !pauseLLM)
				{	// command buffer is empty, result is not empty.
					LLE_Loader.SendMessageToLLM(toLLM.ToString());
					toLLM.Clear();
				}
			}

			FromLLM m;
			for(int i = 0; i < 10; ++i)
			{	if (!LLE_Loader.GetChunkFromLLM(out m)) break;

				if(m.Type != lastMessageType)
				{	
					MyConsole.AddMultiline("\n", Color.LightGray);

					if(lastMessageType == MessageType.Reasoning)
					{	Log($"llmReasoning:\n{llmReasoning}");
						llmReasoning.Clear();
					}
					else if(lastMessageType == MessageType.Content)
					{	Log($"llmContent:\n{llmContent}");
						// content consumer is the command handler
					}
					
					lastMessageType = m.Type;					
				}

				if(m.Type == MessageType.Reasoning)
				{	MyConsole.AddMultiline(m.Payload, Color.LightGray);
					llmReasoning.Append(m.Payload);
				}
				else if(m.Type == MessageType.Content)
				{	MyConsole.AddMultiline(m.Payload, Color.Cyan);
					llmContent.Append(m.Payload);
				}
			}
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
				{	Collisions.Draw(selectedGrid, block);
					//DrawTraversability(block);
				}
			}

			if(Debug.grid != null)
			{	foreach(var cell in Debug.highlightCells)
					Utilities.HighlightCell(Debug.grid, cell, Color.Brown);
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
