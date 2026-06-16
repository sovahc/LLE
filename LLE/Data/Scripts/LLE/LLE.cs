using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
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

		const int AStarBorder = 1;
		internal static AStar astar;
		public static List<Vector3I> path = new List<Vector3I>();

		internal static void Start(IMyCubeGrid grid_)
		{	grid = grid_;

			astarStart = null;
			astarGoal = null;
			
			var gridSize = grid.Max - grid.Min + 1;
			var astarSize = gridSize + AStarBorder * 2;
			astar = new AStar(astarSize, new TraversabilityCalculator(grid_, AStarBorder));
		}

		internal static void Pathfinding(Vector3D point)
		{
			if (grid == null) return;

			var cell = grid.WorldToGridInteger(point);

			Utilities.HighlightCell(grid, cell, Color.Gray);

			if (MyAPIGateway.Input.IsNewLeftMousePressed())
			{
				astarStart = cell;
				MyConsole.Add($"A* start: {cell}", Color.Green);
			}
			if (MyAPIGateway.Input.IsNewRightMousePressed())
			{
				astarGoal = cell;
				MyConsole.Add($"A* goal: {cell}", Color.Red);
			}

			if (MyAPIGateway.Input.IsNewLeftMousePressed() ||
				MyAPIGateway.Input.IsNewRightMousePressed())
			{
				if(astarStart != null && astarGoal != null)
				{
					astar.Reset();
					astar.RunCalculation((Vector3I)astarStart - grid.Min + AStarBorder,
						(Vector3I)astarGoal - grid.Min + AStarBorder);
				}
			}

			if(!astar.Completed())
			{	astar.Iteration();

				if(astar.Completed())
				{
					var result = astar.result;

					MyConsole.Add($"A* path length: {result.Count}", Color.DarkMagenta);

					path.Clear();
					for(int i = 0; i < result.Count; ++i)
					{	path.Add(result[i] + grid.Min - AStarBorder);
					}
				}
			}
		}

		internal static void Draw()
		{
			if(grid == null) return;

			foreach(var cell in path)
			{	Drawing.RoundMarker(grid.GridIntegerToWorld(cell), Color.Yellow);
			}

			if(astarStart != null) Drawing.RoundMarker(grid.GridIntegerToWorld((Vector3I)astarStart), Color.Green);
			if(astarGoal != null) Drawing.RoundMarker(grid.GridIntegerToWorld((Vector3I)astarGoal), Color.Red);
		}
	}

	[MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
	public class LLE : MySessionComponentBase
	{
		private static Font font;

		private Commands commands;

		private readonly StringBuilder llmReasoning = new StringBuilder();
		private readonly StringBuilder llmContent = new StringBuilder();
		private readonly StringBuilder commandToProcess = new StringBuilder();
		private readonly StringBuilder toLLM = new StringBuilder();

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

		private void CommandResult(string result)
		{
			if(result == null) return;

			Log($"CommandResult:\n{result}");
					
			toLLM.Append("\n[RESULT]:\n");
			toLLM.Append(result);
			toLLM.Append('\n');

			MyConsole.AddMultiline(result, Color.Green);
		}

		MessageType lastType = MessageType.Stop;

		public override void UpdateBeforeSimulation()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			Vision.Tick(Utilities.GetEngineerCenter(ch));

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
			string trimmed = content.Trim();
			int lastNewline = trimmed.LastIndexOf('\n');
			string lastLine = lastNewline >= 0 ? trimmed.Substring(lastNewline + 1) : trimmed;

			const string prefix = "Execute `";
			if (!lastLine.StartsWith(prefix))
			{
				CommandResult("ERROR: Last line must start with 'Execute `command`', e.g.: Execute `fly 10 0 0`");
				return;
			}

			int closingBacktick = lastLine.IndexOf('`', prefix.Length);
			if (closingBacktick < 0)
			{
				CommandResult("ERROR: Missing closing backtick in command.");
				return;
			}

			string command = lastLine.Substring(prefix.Length, closingBacktick - prefix.Length);

			//toLLM.Append($"Execute `{command}`");
			toLLM.Append(content);

			if(command == "pause")
			{	pauseLLM = true;
				return;
			}

			string result = commands.Execute(command);
			CommandResult(result);
		}

		public override void Draw()
		{
			var player = MyAPIGateway.Session.Player;
			if (player == null) return;
			var ch = player.Character;
			if (ch == null) return;

			var pm = ch.GetHeadMatrix(false, false);

			Common.StartFrame();

			if(!LLE_Loader.IsPresent())
			{	font.String("LLE_Loader is not present.", new Vector2D(0.0, -0.1), 0.00075f, Color.Red);
				Common.Call_Add_Billboards();
				return;
			}

			var headMatrix = ch.GetHeadMatrix(false);
			Vector3D ahead = headMatrix.Translation + headMatrix.Forward * 10;

			Debug.Pathfinding(ahead);
			Debug.Draw();

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

			MyConsole.Add(message, Color.Magenta);
			if(message.StartsWith(">"))
			{	pauseLLM = true;
				var command = message.Substring(1).Trim();
				ProcessLlmContent($"Execute `{command}`");
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
