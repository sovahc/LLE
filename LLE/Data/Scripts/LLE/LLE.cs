using System.Collections.Generic;
using System.Runtime.CompilerServices;
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

		IMyCubeGrid selectedGrid;
		Vector3I selectedBlock;
		Commands commands;

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

			if(commands == null) commands = new Commands(ch);

			commands.Update();

			if (commands.commandResult != null)
			{	MyConsole.AddMultiline(commands.commandResult, Color.OrangeRed);
				LLE_Loader.SetResult(commands.commandResult);
				commands.commandResult = null;
			}

			MessageFromLLM m;
			if (LLE_Loader.GetMessageFromLLM(out m))
			{	MyConsole.Add(m.Payload, Color.Cyan);
				//commands.Execute(m.Payload);
			}

			// Poll streaming chunks
			//if (!string.IsNullOrEmpty(LLE_Loader.StreamingReasoning)) MyConsole.Add(LLE_Loader.StreamingReasoning, Color.Gray);
			//if (!string.IsNullOrEmpty(LLE_Loader.StreamingContent)) MyConsole.Add(LLE_Loader.StreamingContent, Color.Cyan);
			/*
				ServerCommand cmd;
				if (LLE_Loader.GetCommand(out cmd))
				{	MyConsole.Add(cmd.Payload, Color.Cyan);
					commands.Execute(cmd.Payload);
				}

				if (commands.commandResult != null)
				{	MyConsole.AddMultiline(commands.commandResult, Color.PaleVioletRed);
					LLE_Loader.SetResult(commands.commandResult);
					commands.commandResult = null;
				}
			}*/
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

			MyConsole.AddMultiline(message, Color.Chocolate);
			LLE_Loader.SetChat(player.DisplayName, message);
		}
	}

	public static class LLE_Loader
	{
		public static bool IsPresent() => false;
		public static void Update() { }
		public static void SetChat(string author, string text) { }
		public static bool GetMessageFromLLM(out MessageFromLLM cmd) { cmd = null; return false; }
		public static void SetResult(string result) { }
	}
}
