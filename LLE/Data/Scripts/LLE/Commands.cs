using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

/*
# Command Reference

info 'name'    Get detailed information about a specific block.
search 'name'  Find blocks by name. Returns a list sorted by distance with status (e.g., `Reactor 1: 50m [fuel: 1kg]`).
move_to 'name' Navigate to a specific block. Executes flight with periodic reports.
grind 'name'   Grind a specific block.
weld 'name'    Weld a specific block.
mine 'name'    Mine a specific ore deposit.
status         Check bot status: Battery, Oxygen, Cargo, Hull Integrity.
stop           Immediately cancel the current action and return to IDLE.
vision         Get current visual input (what the bot sees right now).

## Execution Rules

* Time Limits: All actions (`move_to`, `grind`, `weld`, `mine`) have a maximum execution time.
* Reports: Long-running actions provide status updates every N seconds.
* Interruption: Any action can be interrupted by the `stop` command.
* Ambiguity: If a command target is ambiguous (e.g., multiple blocks with the same name), the bot returns a list of options instead of executing.
*/

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

		private static readonly Dictionary<string, string[]> TerminalBCategories = new Dictionary<string, string[]>
		{
			{ "Control", new[] { "Cockpit" } },
			{ "Energy", new[] { "Reactor", "Battery", "SolarPanel" } },
			{ "Defense", new[] { "Turret", "Warhead", "Decoy" } },
			{ "Construction", new[] { "ShipGrinder", "ShipWelder" } },
			{ "Mining", new[] { "OreDetector", "ShipDrill" } },
			{ "Communication", new[] { "Antenna", "Transponder" } }, // << ?
			{ "Production", new[] { "Refinery", "Assembler", "UpgradeModule" } },
			{ "Docking", new[] { "Connector", "Collector" } },
			{ "Gas", new[] { "OxygenGenerator", "OxygenTank", "AirVent" } },
			{ "Life Support", new[] { "CryoChamber", "MedicalRoom" } },
			{ "Computers", new[] { "EventControllerBlock", "TimerBlock", "BroadcastController", "TurretControlBlock", "SensorBlock" } },
			{ "Doors", new[] { "Door" } },
			{ "Gravity", new[] { "GravityGenerator", "VirtualMass", "SpaceBall" } },
			{ "Rotors", new[] { "MotorAdvancedStator", "MotorStator", "Hinge" } },
			{ "Movement", new[] { "Thrust", } },
			{ "Storage", new[] { "CargoContainer" } },
			{ "Decoration", new[] { "HeatVent", "LCDPanel", "TerminalBlock" } }
			//{ "Other", new[] { "ButtonPanel", "Jukebox", "CameraBlock", "SoundBlock", "InteriorLight" } },
			//{ "Structure", new[] { ,  } },
		};

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
			}

			var categorized = new Dictionary<string, List<KeyValuePair<MyObjectBuilderType, int>>>();
			int total = 0;
			foreach (var kv in count)
			{
				string type = kv.Key.ToString();
				if(type.StartsWith(removeIt)) type = type.Substring(removeIt.Length);
				total += kv.Value;
				
				string category = "Other";
				foreach (var cat in TerminalBCategories)
				{
					if (cat.Value.Any(keyword => type.Contains(keyword)))
					{
						category = cat.Key;
						break;
					}
				}
				
				if (!categorized.ContainsKey(category))
					categorized[category] = new List<KeyValuePair<MyObjectBuilderType, int>>();
				categorized[category].Add(kv);
			}
			
			foreach (var cat in categorized.OrderBy(c => c.Key))
			{
				sb.Append($"\n### {cat.Key}\n");
				foreach (var kv in cat.Value)
				{
					string type = kv.Key.ToString();
					if(type.StartsWith(removeIt)) type = type.Substring(removeIt.Length);
					sb.Append($"* {type} → {kv.Value}\n");
				}
			}
			sb.Append($"\n# Total: {total}\n");

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
