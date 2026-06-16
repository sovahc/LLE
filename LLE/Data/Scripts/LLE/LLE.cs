using System.Collections.Generic;
using System.Text;
using Sandbox.ModAPI;
using Sandbox.Game.Entities;

using VRageMath;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using System.CodeDom.Compiler;

namespace LLE
{
	public static class Time { public static double Now => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds; }

	public static class Debug
	{
		public static IMyCubeGrid grid;
		public static List<Vector3I> highlightCellsRed = new List<Vector3I>();
		public static List<Vector3I> highlightCellsGreen = new List<Vector3I>();
		public static AsteroidNavigation asteroidNav;
		public static MyVoxelBase currentAsteroid;
		public static readonly List<MyVoxelBase> voxelSearchList = new List<MyVoxelBase>();
		public static OctreeNode pathStart, pathGoal;
		public static List<OctreeNode> pathNodes;

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

			if(Debug.grid != null)
			{	foreach(var cell in Debug.highlightCellsRed)
					Utilities.HighlightCell(Debug.grid, cell, Color.Red);
				foreach(var cell in Debug.highlightCellsGreen)
					Utilities.HighlightCell(Debug.grid, cell, Color.Green);
			}

			// Auto-detect nearest asteroid within 500m
			Debug.voxelSearchList.Clear();
			var searchSphere = new BoundingSphereD(pm.Translation, 500);
			MyGamePruningStructure.GetAllVoxelMapsInSphere(ref searchSphere, Debug.voxelSearchList);

			MyVoxelBase nearestAsteroid = null;
			double nearestDistSq = double.MaxValue;
			for (int i = 0; i < Debug.voxelSearchList.Count; i++)
			{
				var vb = Debug.voxelSearchList[i];
				if (vb == vb.RootVoxel)
				{
					var distSq = Vector3D.DistanceSquared(vb.PositionComp.GetPosition(), pm.Translation);
					if (distSq < nearestDistSq)
					{
						nearestAsteroid = vb;
						nearestDistSq = distSq;
					}
				}
			}
			Debug.voxelSearchList.Clear();

			if (nearestAsteroid != Debug.currentAsteroid)
			{
				Debug.currentAsteroid = nearestAsteroid;
				if (nearestAsteroid != null && nearestAsteroid.Storage != null)
				{
					Debug.asteroidNav = new AsteroidNavigation(nearestAsteroid);
				}
				else
				{
					Debug.asteroidNav = null;
				}
			}

			if (Debug.asteroidNav != null)
			{
				var ahead = pm.Translation + pm.Forward * 10;

				var nav = Debug.asteroidNav;
				font.String(nav.Statistic.ToString(), new Vector2D(0, 0.85f), 0.00075f, Color.White);
				var currentNode = nav.GetNodeAtWorld(ahead);

				if (currentNode != null)
				{
					DrawOctNode(Debug.asteroidNav, currentNode, true);
				}

				if (MyAPIGateway.Input.IsNewLeftMousePressed())
				{
					Debug.pathStart = nav.GetNodeAtWorld(ahead);
					Debug.pathGoal = null;
					Debug.pathNodes = null;
				}

				if (MyAPIGateway.Input.IsNewRightMousePressed())
				{
					Debug.pathGoal = nav.GetNodeAtWorld(ahead);
					if (Debug.pathStart != null && Debug.pathGoal != null)
						Debug.pathNodes = nav.FindPath(Debug.pathStart, Debug.pathGoal);
				}

				// Draw path markers
				if (Debug.pathStart != null) Drawing.RoundMarker(nav.NodeToWorldBB(Debug.pathStart).Center, Color.Yellow);
				if (Debug.pathGoal != null) Drawing.RoundMarker(nav.NodeToWorldBB(Debug.pathGoal).Center, Color.Magenta);
				if (Debug.pathNodes != null)
				{
					for (int i = 0; i < Debug.pathNodes.Count; i++)
						Drawing.RoundMarker(nav.NodeToWorldBB(Debug.pathNodes[i]).Center, Color.Cyan);
				}
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
