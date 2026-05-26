using System;
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

		public static void Clear()
		{	
			result.Clear();
			foreach(var b in data.Values) b.Clear();
		}

		public static void Append(string s)
		{	
			result.Append(s);
			result.Append('\n');
		}

		public static void Add(string category, string element)
		{
			StringBuilder b;

			if(data.TryGetValue(category, out b))
			{	b.Append(element);
				b.Append('\n');
				return;
			}
			
			b = new StringBuilder();
			b.Append(element);
			b.Append('\n');
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

		private static string Distance(double d)
		{
			if (d < 1000)
				return $"{(int)Math.Round(d, 0, MidpointRounding.AwayFromZero)}m";
			return $"{d / 1000.0:F1}km";
		}

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

		internal static string GridInfo(IMyCubeGrid grid)
		{
			MyMarkdown.Clear();
			MyMarkdown.Append($"# {GridType(grid)} '{grid.DisplayName}'\n");
			MyMarkdown.Append($"(Name → count)");

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

			count.Clear();
			count2.Clear();
			blocks.Clear();
			ts.GetBlocks(blocks);

			//ts.CanAccess()

			int total = 0;
			foreach (var block in blocks)
			{
				var type = block.BlockDefinition.TypeId;
				if (!count.ContainsKey(type))
					count[type] = 0;
				++count[type];
				++total;
			}

			foreach (var kv in count)
			{
				string type = kv.Key.ToString();
				if(type.StartsWith(removeIt)) type = type.Substring(removeIt.Length);

				string category = "Other";
				foreach (var cat in TerminalBCategories)
				{
					if (cat.Value.Any(keyword => type.Contains(keyword)))
					{
						category = cat.Key;
						break;
					}
				}

				MyMarkdown.Add(category, $"* {type} → {kv.Value}");
			}
			
			return MyMarkdown.Result();
		}

		private static bool Include(string searchTerm, string data)
		{	if(searchTerm == "" || searchTerm == "*") return true;
			return data.Contains(searchTerm);
		}

		private static void Description(MyEntity e, out string category, out string name)
		{
			category = "Unknown";
			name = e.DisplayName;
			if(name == null) name = e.ToString();

			var grid = e as IMyCubeGrid;
			if (grid != null)
			{	if(grid.IsStatic) category = "STATION";
				else if(grid.GridSizeEnum == MyCubeSize.Large) category = "LARGE GRID";
				else if(grid.GridSizeEnum == MyCubeSize.Small) category = "SMALL GRID";
				return;
			}
			var voxel = e as MyVoxelBase;
			if (voxel != null)
			{	if (voxel is MyPlanet)
				{	category = "PLANET";
					return;
				}
				category = "ASTEROID";
				return;
			}

			var floater = e as IMyFloatingObject;
			if (floater != null)
			{	category = "FLOATING OBJECT";
				return;
			}
		}
	
		internal static string Search(string query, Vector3D center, int radius = 500)
		{
			query = query.Trim(MyTrim);
			
			MyMarkdown.Clear();
			MyMarkdown.Append($"# SEARCH RESULT '{query}' (RADIUS {Distance(radius)})");

			BoundingSphereD S = new BoundingSphereD(center, radius);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				double distance = (e.WorldMatrix.Translation - center).Length();

				string category, name;

				Description(e, out category, out name);

				if(Include(query, name) || Include(query, category))
					MyMarkdown.Add($"## {category}", $"* {Quotes(name)} → {Distance(distance)}");
			}

			return MyMarkdown.Result();
		}

		internal static void Fly(string to, Vector3D center)
		{
			to = to.Trim(MyTrim);

			BoundingSphereD S = new BoundingSphereD(center, 1000);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);
			
			List<MyEntity> matches = new List<MyEntity>();

			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				double distance = (e.WorldMatrix.Translation - center).Length();

				var grid = e as IMyCubeGrid;
				if (grid != null)
				{	
					if(Include(to, grid.DisplayName)) matches.Add(e);
					continue;
				}

				var voxel = e as MyVoxelBase;
				if (voxel != null)
				{	if (voxel is MyPlanet) continue;

					if(Include(to, voxel.DebugName)) matches.Add(e);
					continue;
				}

				var floater = e as IMyFloatingObject;
				if (floater != null)
				{	
					if(Include(to, floater.DisplayName)) matches.Add(e);
					continue;
				}
			}

			///

		}
    }
}
