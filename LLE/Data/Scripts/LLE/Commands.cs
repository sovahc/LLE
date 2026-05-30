using System;
using System.Collections.Generic;
using System.ComponentModel;
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
	public static class Formatter
	{
		public static string Remove_MyObjectBuilder_(string type)
		{
			if (type.StartsWith("MyObjectBuilder_")) type = type.Substring("MyObjectBuilder_".Length);
			return type;
		}

		public static string Quote(string s)
		{	if(s == null) return "(null)";
			if(!s.Contains(' ')) return s;
			return $"'{s}'";
		}

		public static string Distance(double d)
		{
			if (d < 1000)
				return $"{(int)Math.Round(d, 0, MidpointRounding.AwayFromZero)}m";
			return $"{d / 1000.0:F1}km";
		}

		public static string IJK(Vector3I v)
		{	return $"{v.X} {v.Y} {v.Z}";
		}

		public static void Description(MyEntity e, out string category, out string name)
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

		public static string Description(IMySlimBlock block)
		{    
			if(block == null) return "none";
			return block.BlockDefinition.DisplayNameText;
		}
	}

	public class Commands
	{
		enum Action
		{	Idle,
			Flying,
			Welding,
			Grinding,			
		}

		private Action currentAction = Action.Idle;

		private IMyCubeGrid selectedGrid;
		private MyVoxelBase selectedAsteroid;

		private readonly IMyCharacter character;
		
		private Navigation navigation;
		private BotTools botTools;

		private readonly StringBuilder tmp = new StringBuilder();

		private static readonly char[] MyTrim = new char [] {' ', '\t', '"', '\''};

		public string commandResult;

		public Commands(IMyCharacter character_)
		{	character = character_;			
		}

		internal void Help()
		{
			commandResult = @"# Command Reference

* select_grid 'name'		- Select ship or station on which grind, weld and other operations will be performed.
* select_asteroid 'name'	- Select asteroid on which mine operations will be performed.

* fly I J K					- Fly to specific grid coordinates (integer values)
* grind I J K				- Grind a block at specific coordinates.
* weld I J K				- Weld a block at specific coordinates.
* nearest					- Return 6 accesiible blocks around you, an the block you stand on
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
		

		private static bool Include(string searchTerm, string data)
		{	if(searchTerm == "" || searchTerm == "*") return true;
			return data.Contains(searchTerm);
		}

		private string MyError(Vector3D engineer, string query, List<MyEntity> matches)
		{
			if(matches.Count == 0)
				return $"Error: object '{query}' not found, use the exact object name.";

			string message;
			tmp.Clear();
			tmp.Append($"Error: multiple objects match '{query}':\n");
			foreach (var e in matches)
			{
				string category, name;
				Formatter.Description(e, out category, out name);
				double distance = (e.WorldMatrix.Translation - engineer).Length();
				tmp.Append($"* {category} {Formatter.Quote(name)} → {Formatter.Distance(distance)}\n");
			}
			tmp.Append("\n\n");
			message = tmp.ToString();
			return message;
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
		internal void Select(ObjectType type, Vector3D engineer, string query, int radius = 1000)
		{
			query = query.Trim(MyTrim);
			
			BoundingSphereD S = new BoundingSphereD(engineer, radius);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);

			List<MyEntity> matches = new List<MyEntity>();

			string category, name;
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				Formatter.Description(e, out category, out name);

				if(Include(query, name) || Include(query, category)) matches.Add(e);
			}

			if(matches.Count != 1)
			{	commandResult = MyError(engineer, query, matches);
				return;
			}

			Formatter.Description(matches[0], out category, out name);
			commandResult = $"Selected {category} {Formatter.Quote(name)}";

			switch(type)
			{	case ObjectType.LargeShip:
					selectedGrid = matches[0] as IMyCubeGrid;
					if(selectedGrid == null) commandResult = $"Error: {Formatter.Quote(name)} is {category}";
					return;
				case ObjectType.Asteroid:
					selectedAsteroid = matches[0] as MyVoxelBase;
					if(selectedAsteroid == null) commandResult = $"Error: {Formatter.Quote(name)} is {category}";
					return;
				default:
					commandResult = "Internal error";
					return;
			}
		}

		private bool TryParseIJK(string s, out Vector3I v)
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
			commandResult = $"Error: invalid vector '{s}', use integer numbers e.g `command -1 5 2`";
			return false;
		}

		private bool GridIsSet()
		{	if(selectedGrid == null)
			{	commandResult = $"Error: you should select a grid first, use `select_grid name`";
				return false;
			}
			return true;
		}

		internal void Fly(string arguments)
		{	
			Vector3I to;

			if(!GridIsSet()) return;
			if(!TryParseIJK(arguments, out to)) return;

			navigation.FlyInsideGrid(selectedGrid, to);
			currentAction = Action.Flying;
		}

		internal void Grind(string arguments)
		{
			Vector3I what;

			if(!GridIsSet()) return;
			if(!TryParseIJK(arguments, out what)) return;
			
			var block = selectedGrid.GetCubeBlock(what);
			if(block == null)
			{	commandResult = $"Error: no block at {what}";
				return;
			}

			botTools.SetTargetBlock(block);

			currentAction = Action.Grinding;
			commandResult = "Grinding...";
		}

		internal void Weld(string arguments)
		{
			Vector3I what;

			if(!GridIsSet()) return;
			if(!TryParseIJK(arguments, out what)) return;
			
			var block = selectedGrid.GetCubeBlock(what);
			if(block == null)
			{	commandResult = $"Error: no block at {what}";
				return;
			}

			botTools.SetTargetBlock(block);

			currentAction = Action.Welding;
			commandResult = "Welding...";
		}

		internal void Nearest(string arguments)
		{	
			if(!GridIsSet()) return;

			MyMarkdown.Clear();

			if(arguments != "") MyMarkdown.Append("Warning: Nearest9 doesn't support arguments\n");

			var center = Utilities.GetEngineerCenter(character);
			var cI = selectedGrid.WorldToGridInteger(center);

			var name = Formatter.Description(selectedGrid.GetCubeBlock(cI));
			MyMarkdown.Append($"## Your current position: {cI} Block: {Formatter.Quote(name)}");

			int added = 0;
			Debug.grid = selectedGrid;
			Debug.highlightCells.Clear();

			foreach (var direction in Constants.SixDirections)
			{	var v = cI + direction;
				var block = selectedGrid.GetCubeBlock(v);

				if(block == null) continue;

				Debug.highlightCells.Add(v);

				name = Formatter.Description(block);

				MyMarkdown.Add(Formatter.Quote(name), $"{Formatter.IJK(v)}");
				++added;
			}
			if(added == 0)
			{	MyMarkdown.Append("No any blocks around you.");
			}

			commandResult = MyMarkdown.Result();
		}

		internal void Update()
		{
			if(botTools == null) botTools = new BotTools(character);
			if(navigation == null) navigation = new Navigation(character);

			switch(currentAction)
			{	case Action.Idle:
					return;
				case Action.Flying:
					if(!navigation.Step())
						currentAction = Action.Idle;
					return;
				case Action.Grinding:
					if(!botTools.GrindBlock())
					{	botTools.Stop();
						MyConsole.Add("Stop");
						currentAction = Action.Idle;
					}
					return;
				case Action.Welding:
					if(!botTools.WeldBlock())
					{	botTools.Stop();
						MyConsole.Add("Stop");
						currentAction = Action.Idle;
					}
					return;
			}
		}
	}
}
