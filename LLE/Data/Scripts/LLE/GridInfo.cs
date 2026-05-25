using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace LLE
{
	/*public class BlockInfo
	{
		public IMyTerminalBlock Block;
		public string Name;
		public string Type;
		public bool IsWorking;
		public bool IsFunctional;
		public long EntityId;
		public Vector3I Position;
		public long OwnerId;
	}*/

	public class GridInfo
	{
		private static void Log(string s)
		{
			MyConsole.Add(s, Color.Gray);
			MyLog.Default.WriteLine("LLE " + s);
		}

		private readonly Dictionary<MyObjectBuilderType, int> count = new Dictionary<MyObjectBuilderType, int>();
		private readonly Dictionary<MyDefinitionId, int> count2 = new Dictionary<MyDefinitionId, int>();
		private readonly List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

		private readonly string removeIt = "MyObjectBuilder_";

		public void Info(IMyCubeGrid grid)
		{
			string gridType = "";
			if(grid.IsStatic) gridType = "Station";
			else if(grid.GridSizeEnum == MyCubeSize.Large) gridType = "Large Grid";
			else if(grid.GridSizeEnum == MyCubeSize.Small) gridType = "Small Grid";

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

			count.Clear();
			count2.Clear();
			blocks.Clear();

			ts.GetBlocks(blocks);

			StringBuilder sb = new StringBuilder();
			sb.Append($"## {gridType} '{grid.DisplayName}'\n");
			sb.Append($"# Name → count\n");

			//ts.CanAccess()

			foreach (var block in blocks)
			{
				var type = block.BlockDefinition.TypeId;
				if (!count.ContainsKey(type))
					count[type] = 0;
				++count[type];

				//var def = new MyDefinitionId(block.BlockDefinition.TypeId, block.BlockDefinition.SubtypeId);
				//if (!count2.ContainsKey(def))
				//	count2[def] = 0;
				//++count2[def];
			}

			foreach (var kv in count)
			{	
				string type = kv.Key.ToString();
				if(type.StartsWith(removeIt)) type = type.Substring(removeIt.Length);
				sb.Append($"* {type} → {kv.Value}\n");
			}

			Log($"\n\n{sb}\n");

			//foreach (var kv in count2)
			//	Log($"{kv.Key}: {kv.Value}");

			//Log($"{block.DisplayNameText}");
			//Log($"{block.BlockDefinition.TypeIdString} {type} {block.BlockDefinition.SubtypeIdAttribute}");
			//Log($"{block.IsWorking} {block.IsFunctional}");
			//Log($"{block.Position}");
			//Log($"{block.OwnerId}");
		}
	}
}
