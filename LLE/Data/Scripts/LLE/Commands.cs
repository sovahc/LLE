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
using VRageMath;

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
		private static IMyCubeGrid selectedGrid;
		private static MyVoxelBase selectedAsteroid;

		private static readonly Dictionary<MyObjectBuilderType, int> count = new Dictionary<MyObjectBuilderType, int>();
		private static readonly List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

		private static readonly StringBuilder tmp = new StringBuilder();

		private static readonly char[] MyTrim = new char [] {' ', '\t', '"', '\''};

		public static void Help(out string message)
		{
			message = @"# Command Reference

* select_grid 'name'		- Select ship or station on which grind, weld and other operations will be performed.
* select_asteroid 'name'	- Select asteroid on which mine operations will be performed.

* nearest9					- Return 9 blocks around you, including the block you stand on
* fly I J K					- Fly to specific grid coordinates (integer values)
* grind I J K				- Grind a block at specific coordinates.
* weld I J K				- Weld a block at specific coordinates.
";
		}

/*


inventory              - Return bot inventory items.
vision                 - Get current visual input (what the bot sees right now)
help                   - this message.
cancel                 - Immediately cancel the current action and return to IDLE.
stop                   - Stop movement.
nearest ['substring']  - Show the nearest 5 blocks whose names contain 'substring'.
search ['substring']   - Find any objects by partial match. Ex: `search` (search anything), `search STATION`, `search Steel Plate`
info 'name'        - Get detailed information about a specific object.
fly 'name'         - Fly to a specific object. Executes flight with periodic reports.
look at 'name'     - Just rotate to the object
hack 'block_name'  - Grind a specific block just below the hacking point (weld it back to restore functionality).
weld 'block_name'  - Weld a specific block.
mine 'block_name'  - Mine a specific ore deposit.
status             - Check bot status: Battery, Hydrogen, Oxygen.
pickup 'name'      - Pick up a specified object.
drop 'name' [quantity|all] - Drop a specified object.
get 'item' from 'block name'
put 'item' into 'block name'
? move {forward|backward|left|right|up|down} {distance} - move in direction
? recover from stuck
? save to memory 'string'

## Execution Rules

* Reports: Long-running actions provide status updates every 5 seconds.
* Interruption: Any action can be interrupted by the `cancel` command.
* Ambiguity: If a command target is ambiguous (e.g., multiple blocks with the same name),
* the execution layer returns an error and a list of options instead of executing.

Path finding: safest (default) / shortest / scouting / prefer open space

*/

		private static string Quotes(string s)
		{	if(s == null) return "(null)";
			if(!s.Contains(' ')) return s;
			return $"'{s}'";
		}

		private static string Distance(double d)
		{
			if (d < 1000)
				return $"{(int)Math.Round(d, 0, MidpointRounding.AwayFromZero)}m";
			return $"{d / 1000.0:F1}km";
		}

		private static bool Include(string searchTerm, string data)
		{	if(searchTerm == "" || searchTerm == "*") return true;
			return data.Contains(searchTerm);
		}

		private static string MyError(Vector3D engineer, string query, List<MyEntity> matches)
		{
			if(matches.Count == 0)
				return $"Error: object '{query}' not found, use the exact object name.";

			string message;
			tmp.Clear();
			tmp.Append($"Error: multiple objects match '{query}':\n");
			foreach (var e in matches)
			{
				string category, name;
				Description(e, out category, out name);
				double distance = (e.WorldMatrix.Translation - engineer).Length();
				tmp.Append($"* {category} {Quotes(name)} → {Distance(distance)}\n");
			}
			tmp.Append("\n\n");
			message = tmp.ToString();
			return message;
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
/*
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
			{ "Computers", new[] { "EventController", "Timer", "BroadcastController", "TurretControl", "Sensor" } },
			{ "Doors", new[] { "Door" } },
			{ "Gravity", new[] { "GravityGenerator", "VirtualMass", "SpaceBall" } },
			{ "Rotors", new[] { "MotorAdvancedStator", "MotorStator", "Hinge" } },
			{ "Movement", new[] { "Thrust" } },
			{ "Storage", new[] { "CargoContainer" } },
			{ "Decoration", new[] { "HeatVent", "LCDPanel", "Terminal" } }
			//{ "Other", new[] { "ButtonPanel", "Jukebox", "CameraBlock", "SoundBlock", "InteriorLight" } },
			//{ "Structure", new[] { ,  } },
		};

		internal static string GridInfo(IMyCubeGrid grid)
		{
			MyMarkdown.Clear();
			MyMarkdown.Append($"# {GridType(grid)} '{grid.DisplayName}'");
			MyMarkdown.Append($"(Name → count)");

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);

			count.Clear();
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
				type = Remove_MyObjectBuilder_(type);

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
*/
/*
		internal static string Search(Vector3D center, int radius, string query)
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
*/
/*
		internal static bool Fly(Vector3D from, string to, out string message, out Vector3D point)
		{
			to = to.Trim(MyTrim);

			BoundingSphereD S = new BoundingSphereD(from, 1000);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);
			
			List<MyEntity> matches = new List<MyEntity>();

			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				double distance = (e.WorldMatrix.Translation - from).Length();

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

			if(matches.Count != 1)
			{	message = MyError(from, to, matches);
				point = Vector3D.Zero;
				return false;
			}

			message = "Executing...";
			point = matches[0].WorldMatrix.Translation;
			return true;
		}
*/

/*		internal static void Nearest_blocks(Vector3D engineer, string query, out string message)
		{
			if(selectedGrid != null && selectedGrid.Closed) selectedGrid = null;

			if(selectedGrid == null)
			{	message = "You should select a grid first using command: `select_grid name`.";
				return;
			}

			int radius = 10;
			query = query.Trim(MyTrim);

			var bs = new BoundingSphereD(engineer, radius);
			var blocks = selectedGrid.GetBlocksInsideSphere(ref bs);

			MyMarkdown.Clear();
			MyMarkdown.Append($"# NEAREST BLOCKS MATCHES '{query}' (RADIUS {Distance(radius)})");
			foreach(var b in blocks)
			{
				if(b.FatBlock == null)
				{	MyMarkdown.Add("SLIM BLOCKS", SlimBlockDescription(b));
				}
				else
				{	MyMarkdown.Add("FAT BLOCKS", SlimBlockDescription(b));
				}
			}

			message = MyMarkdown.Result();
		}
*/
		internal static void Select(ObjectType type, Vector3D engineer, string query, out string message, int radius = 1000)
		{
			query = query.Trim(MyTrim);
			
			BoundingSphereD S = new BoundingSphereD(engineer, radius);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);

			List<MyEntity> matches = new List<MyEntity>();

			string category, name;
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				Description(e, out category, out name);

				if(Include(query, name) || Include(query, category)) matches.Add(e);
			}

			if(matches.Count != 1)
			{	message = MyError(engineer, query, matches);
				return;
			}

			Description(matches[0], out category, out name);
			message = $"Selected {category} {Quotes(name)}";

			switch(type)
			{	case ObjectType.LargeShip:
					selectedGrid = matches[0] as IMyCubeGrid;
					if(selectedGrid == null) message = $"Error: {Quotes(name)} is {category}";
					return;
				case ObjectType.Asteroid:
					selectedAsteroid = matches[0] as MyVoxelBase;
					if(selectedAsteroid == null) message = $"Error: {Quotes(name)} is {category}";
					return;
				default:
					message = "Internal error";
					return;
			}
		}

		private static bool TryParseIJK(string s, out Vector3I v)
		{	string[] parts = s.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
			int x, y, z;
			if (parts.Length == 3 && 
				int.TryParse(parts[0], out x) && 
				int.TryParse(parts[1], out y) && 
				int.TryParse(parts[2], out z))
			{
				v = new Vector3I(x, y, z);
				return true;
			}
			v = Vector3I.Zero;
			return false;
		}

		internal static void Fly(IMyCharacter ch, string arguments, out string message)
		{	
			Vector3I to;
			if(!TryParseIJK(arguments, out to))
			{	message = $"Error: invalid vector '{arguments}', use integer numbers, e.g `fly -1 5 2`";
				return;
			}
			if(selectedGrid == null)
			{	message = $"Error: you should select a grid first, use `select_grid name`";
				return;
			}

			message = null;
			MacroNavigation.FlyToGrid(ch, selectedGrid, to);
		}
	}
}


/*		private static string GridType(IMyCubeGrid g)
		{	if(g.IsStatic) return "Station";
			else if(g.GridSizeEnum == MyCubeSize.Large) return "Large Grid";
			else if(g.GridSizeEnum == MyCubeSize.Small) return "Small Grid";
			else return "?";
		}

		private static string Remove_MyObjectBuilder_(string type)
		{
			if (type.StartsWith("MyObjectBuilder_")) type = type.Substring("MyObjectBuilder_".Length);
			return type;
		}

		private static string SlimBlockDescription(IMySlimBlock b)
		{	var type = Remove_MyObjectBuilder_(b.BlockDefinition.Id.TypeId.ToString());
			return type;
		}
*/