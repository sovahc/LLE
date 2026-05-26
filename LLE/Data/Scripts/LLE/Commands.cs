using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

/*
# Command Reference

search 'name'  Find blocks by name. Returns a list sorted by distance with status (e.g., `Reactor 1: 50m [fuel: 1kg]`).
info 'name'    Get detailed information about a specific block.
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
	public class MyMarkdown
	{
		private static readonly Dictionary<string, StringBuilder> data = new Dictionary<string, StringBuilder>();

		private static readonly StringBuilder result = new StringBuilder();

		public static void Start(string header)
		{	foreach(var b in data.Values) b.Clear();
			
			result.Clear();
			result.Append(header);
			result.Append('\n');
		}

		public static void Add(string category, string element)
		{
			StringBuilder b;

			if(data.TryGetValue(category, out b))
			{	if(b.Length > 0) b.Append('\n');
				b.Append(element);
				return;
			}
			
			b = new StringBuilder();
			b.Append(element);
			data[category] = b;
		}

		public static string Result()
		{
			foreach(var kv in data)
			{	
				if(kv.Value.Length > 0)
				{	result.Append(kv.Key);
					result.Append('\n');
					result.Append(kv.Value);
					result.Append('\n');
				}
			}
			return result.ToString();
		}
	}

	public static class Commands
	{
		private static readonly Dictionary<MyObjectBuilderType, int> count = new Dictionary<MyObjectBuilderType, int>();
		private static readonly Dictionary<MyDefinitionId, int> count2 = new Dictionary<MyDefinitionId, int>();
		private static readonly List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

		private static readonly string removeIt = "MyObjectBuilder_";

		private static string GridType(IMyCubeGrid g)
		{	if(g.IsStatic) return "Station";
			else if(g.GridSizeEnum == MyCubeSize.Large) return "Large Grid";
			else if(g.GridSizeEnum == MyCubeSize.Small) return "Small Grid";
			else return "?";
		}

		private static string Quotes(string s)
		{	if(s == null) return "(null)";
			if(!s.Contains(' ')) return s;
			return($"'{s}'");
		}

		private static string Distance(double d) { return $"{d:0.#}m"; }

		private static bool IsVoid(string parameter) { return parameter == "" || parameter == "*"; }
		
		private static readonly char[] MyTrim = new char [] {' ', '\t', '"', '\''};

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

		public static string GridInfo(IMyCubeGrid grid)
		{
/*			string gridType = "";

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

			count.Clear();
			count2.Clear();
			blocks.Clear();

			ts.GetBlocks(blocks);

			ClearBuffers();
			result.Append($"# {gridType} '{grid.DisplayName}'\n");
			result.Append($"## Name → count\n");

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
				result.Append($"\n### {cat.Key}\n");
				foreach (var kv in cat.Value)
				{
					string type = kv.Key.ToString();
					if(type.StartsWith(removeIt)) type = type.Substring(removeIt.Length);
					result.Append($"* {type} → {kv.Value}\n");
				}
			}
			result.Append($"\n# Total: {total}\n");

			return result.ToString();*/
			return "";
		}

		public static string Search(string name, Vector3D center, int radius = 500)
		{
			name = name.Trim(MyTrim);
			
			MyMarkdown.Start($"# SEARCH RESULT '{name}' (RADIUS {radius}m)");

			BoundingSphereD S = new BoundingSphereD(center, radius);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				double distance = (e.WorldMatrix.Translation - center).Length();

				var grid = e as IMyCubeGrid;
				if (grid != null)
				{	// Owners: {grid.BigOwners}

					string description = $"* {Quotes(grid.DisplayName)} → {Distance(distance)}";

					if(grid.IsStatic)
						MyMarkdown.Add("## STATIONS", description);
					else if(grid.GridSizeEnum == MyCubeSize.Large)
						MyMarkdown.Add("## LARGE GRIDS", description);
					else if(grid.GridSizeEnum == MyCubeSize.Small)
						MyMarkdown.Add("## SMALL GRIDS", description);

					continue;
				}

				var voxel = e as MyVoxelBase;
				if (voxel != null)
				{	if (voxel is MyPlanet) continue;
					string description = $"* {Quotes(voxel.DebugName)} → {Distance(distance)}";
					MyMarkdown.Add("## ASTEROIDS", description);
					continue;
				}

				var floater = e as IMyFloatingObject;
				if (floater != null)
				{	string description = $"* {Quotes(floater.DisplayName)} → {Distance(distance)}";
					MyMarkdown.Add("## FLOATING OBJECTS", description);
					continue;
				}
			}

			return MyMarkdown.Result();
		}
	}
}
