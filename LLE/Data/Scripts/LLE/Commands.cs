using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;
using WTF_IMyInventory = VRage.Game.ModAPI.IMyInventory;

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

		public static string Percent(float f)
		{	var ff = (int)Math.Round(f * 100, 0, MidpointRounding.AwayFromZero);
			return $"{ff}%";
		}

		public static string Volume(double d)
		{	var dd = Math.Round(d, 1, MidpointRounding.AwayFromZero);
			return $"{dd:F2}m³";
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

		internal TokenParser tokenParser;
		public string commandResult;

		public Commands(IMyCharacter character_)
		{	character = character_;			
		}

		internal void Help()
		{
			commandResult = @"# Command Reference

* select_grid 'name'    		- Select a ship or station on which to grind, weld, and perform other operations.
* select_asteroid 'name'		- Select an asteroid on which to mine.

* overview						- List grid blocks by category.
* search 'substring'			- Search blocks coordinates by name.
* fly I J K						- Fly to specific grid coordinates (integer values)
* grind I J K					- Grind a block at specific coordinates.
* weld I J K					- Weld a block at specific coordinates.
* near							- Return 6 accessible blocks around you and the block you are standing on.
* inventory						- Return the items in your inventory.
* inventory I J K				- Return the inventory of the container at specific coordinates.
* get count 'item' from I J K	- Transfer an item from a container to your inventory. Ex `get 10 'Gold Ingot' from -1 5 2`
* put count 'item' into I J K	- Transfer an item from your inventory to a container. Ex `put 1 'Medkit' into 14 0 2`
* transfer count 'item' from I1 J1 K1 to I2 J2 K2
								- Transfer an item from one inventory to another.
";
}

/*
vision                 - Get current visual input (what the bot sees right now)
cancel                 - Immediately cancel the current action and return to IDLE.
stop                   - Stop movement.
nearest ['substring']  - Show the nearest 5 blocks whose names contain 'substring'.
search ['substring']   - Find any objects by partial match. Ex: `search` (search anything), `search STATION`, `search Steel Plate`
info 'name'        - Get detailed information about a specific object.
look at 'name'     - Rotate to face the object
hack 'block_name'  - Grind a specific block just below the hacking point (weld it back to restore functionality).
mine 'block_name'  - Mine a specific ore deposit.
status             - Check bot status: Battery, Hydrogen, Oxygen.
pickup 'name'      - Pick up a specified object.
drop 'name' [quantity|all] - Drop a specified object.
? move {forward|backward|left|right|up|down} {distance} - move in a direction
? recover from being stuck
? save to memory 'string'

## Execution Rules

* Reports: Long-running actions provide status updates every 5 seconds.
* Interruption: Any action can be interrupted by the `cancel` command.
* Ambiguity: If a command target is ambiguous (e.g., multiple blocks with the same name),
* the execution layer returns an error and a list of options instead of executing.

Path finding: safest (default) / shortest / scouting / prefer open space

*/
		private static bool Include(string searchTerm, string text)
		{	if(searchTerm == "" || searchTerm == "*") return true;
			return text.Contains(searchTerm);
		}

		private string MyError(Vector3D engineer, string query, List<MyEntity> matches)
		{
			if(matches.Count == 0)
				return $"Error: object '{query}' not found. Use the exact object name.";

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

		private static readonly Dictionary<MyObjectBuilderType, int> count = new Dictionary<MyObjectBuilderType, int>();
		private static readonly List<IMyTerminalBlock> terminalBlocks = new List<IMyTerminalBlock>();
		private static readonly List<IMySlimBlock> slimBlocks = new List<IMySlimBlock>();

		internal void Overview()
		{
			if(!GridIsSet()) return;

			string category, name;
			Formatter.Description(selectedGrid as MyEntity, out category, out name);

			MyMarkdown.Clear();
			MyMarkdown.Append($"# {category} '{name}'");
			MyMarkdown.Append($"(Name → count)");

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(selectedGrid);

			count.Clear();
			terminalBlocks.Clear();
			ts.GetBlocks(terminalBlocks);

			int total = 0;
			foreach (var block in terminalBlocks)
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
				type = Formatter.Remove_MyObjectBuilder_(type);

				category = "Other";
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

			count.Clear();
			terminalBlocks.Clear();

			commandResult = MyMarkdown.Result();
		}

		internal void Search()
		{
			if(!GridIsSet()) return;

			string query = tokenParser.NextString();
			if(query == "")
			{	commandResult = "Error: query should not be ''";
				return;
			}

			string category, name;
			Formatter.Description(selectedGrid as MyEntity, out category, out name);

			MyMarkdown.Clear();
			MyMarkdown.Append($"# SEARCH ON {category} '{name}' QUERY: '{query}'");

			selectedGrid.GetBlocks(slimBlocks);

			foreach(var block in slimBlocks)
			{	name = Formatter.Description(block);

				if(Include(query, name))
				{	MyMarkdown.Add(name, Formatter.IJK(block.Position));
				}
			}

			commandResult = MyMarkdown.Result();
		}

		internal void Select(ObjectType type)
		{
			var what = tokenParser.NextString();

			const int radius = 1000;

			var engineer = Utilities.GetEngineerCenter(character);
			
			BoundingSphereD S = new BoundingSphereD(engineer, radius);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);

			List<MyEntity> matches = new List<MyEntity>();

			string category, name;
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				Formatter.Description(e, out category, out name);

				if(Include(what, name) || Include(what, category)) matches.Add(e);
			}

			if(matches.Count != 1)
			{	commandResult = MyError(engineer, what, matches);
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

		private bool GridIsSet()
		{	if(selectedGrid == null)
			{	commandResult = $"Error: you should select a grid first. Use `select_grid name`";
				return false;
			}
			return true;
		}

		internal void Fly()
		{
			if(!GridIsSet()) return;

			Vector3I ijk;
			if(!tokenParser.NextVector3I(out ijk)) return;

			navigation.FlyInsideGrid(selectedGrid, ijk);
			currentAction = Action.Flying;
		}

		internal void Grind()
		{
			if(!GridIsSet()) return;

			Vector3I ijk;
			if(!tokenParser.NextVector3I(out ijk))
			{	commandResult = "Error: expected I J K";
				return;
			}

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null)
			{	commandResult = $"Error: no block at {ijk}";
				return;
			}

			botTools.SetTargetBlock(block);
			currentAction = Action.Grinding;
			commandResult = "Grinding...";
		}

		internal void Weld()
		{
			if(!GridIsSet()) return;

			Vector3I ijk;
			if(!tokenParser.NextVector3I(out ijk))
			{	commandResult = "Error: expected I J K";
				return;
			}

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null)
			{	commandResult = $"Error: no block at {ijk}";
				return;
			}

			botTools.SetTargetBlock(block);
			currentAction = Action.Welding;
			commandResult = "Welding...";
		}

		internal void Near()
		{	
			if(!GridIsSet()) return;

			MyMarkdown.Clear();

			if(!tokenParser.End) MyMarkdown.Append("Warning: `near` doesn't have arguments\n");

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
			{	MyMarkdown.Append("No blocks around you.");
			}

			commandResult = MyMarkdown.Result();
		}

		internal void Inventory()
		{
			if(tokenParser.End)
			{
				var inv = character.GetInventory() as IMyInventory;
				if (inv == null) { commandResult = "Internal error"; return; }

				tmp.Clear();
				tmp.Append($"Your inventory:\n");
				InventoryToText(inv, tmp);

				commandResult = tmp.ToString();
			}
			else
			{
				if(!GridIsSet()) return;

				Vector3I ijk;
				if(!tokenParser.NextVector3I(out ijk)) return;

				var block = selectedGrid.GetCubeBlock(ijk);
				if(block == null)
				{	commandResult = $"Error: no block at {ijk}";
					return;
				}

				var name = Formatter.Description(block);
				var fat = block.FatBlock;

				if(fat == null || !fat.HasInventory)
				{	commandResult = $"Block {Formatter.Quote(name)} does not have an inventory.";
					return;
				}

				tmp.Clear();
				var es = fat.InventoryCount == 1 ? "" : "es";
				tmp.Append($"Current inventory{es} of {Formatter.Quote(name)} (at {Formatter.IJK(ijk)}):\n");

				for(int i = 0; i < fat.InventoryCount; ++i)
				{	var inv = fat.GetInventory(i);
					InventoryToText(inv, tmp);
				}

				commandResult = tmp.ToString();
			}
		}

		private static void InventoryToText(IMyInventory inv, StringBuilder output)
		{
			output.Append($"Used {Formatter.Volume((double)inv.CurrentVolume)}/{Formatter.Volume((double)inv.MaxVolume)} ({Formatter.Percent(inv.VolumeFillFactor)})\n");

			List<MyInventoryItem> items = new List<MyInventoryItem>();
			items.Clear();
			inv.GetItems(items);

			for (int i = 0; i < items.Count; i++)
			{
				var item = items[i];

				var def = (MyDefinitionId)item.Type;
				var itemDef = MyDefinitionManager.Static.GetDefinition(def) as MyPhysicalItemDefinition;

				var volume = (double)item.Amount * itemDef.Volume;

				output.Append($"* {itemDef.DisplayNameText} → {item.Amount} ({Formatter.Volume(volume)})\n");
			}
		}

		internal void Get()
		{
			if(!GridIsSet()) return;

			double count; Vector3I ijk;

			if(!tokenParser.NextDouble(out count))
			{	commandResult = "Error: expected count";
				return;
			}

			var item = tokenParser.NextString();

			if(!tokenParser.Match("from"))
			{	commandResult = "Error: expected 'from'";
				return;
			}

			if(!tokenParser.NextVector3I(out ijk))
			{	commandResult = "Error: expected I J K";
				return;
			}

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null)
			{	commandResult = $"Error: no block at {ijk}";
				return;
			}

			var fat = block.FatBlock;
			var fromName = Formatter.Description(block);

			if(fat == null || !fat.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(fromName)} does not have an inventory.";
				return;
			}

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			for (int ii = 0; ii < fat.InventoryCount; ++ii)
				fromList.Add(fat.GetInventory(ii));

			toList.Add(character.GetInventory());

			InventoryTransfer(fromList, toList, fromName, "your inventory", item, (MyFixedPoint)count, out commandResult);
		}

		internal void Put()
		{
			if(!GridIsSet()) return;

			double count; Vector3I ijk;

			if(!tokenParser.NextDouble(out count))
			{	commandResult = "Error: expected count";
				return;
			}

			var item = tokenParser.NextString();

			if(!tokenParser.Match("into"))
			{	commandResult = "Error: expected 'into'";
				return;
			}

			if(!tokenParser.NextVector3I(out ijk))
			{	commandResult = "Error: expected I J K";
				return;
			}

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null)
			{	commandResult = $"Error: no block at {ijk}";
				return;
			}

			var inv = character.GetInventory() as IMyInventory;
			if (inv == null) { commandResult = "Internal error"; return; }

			var fat = block.FatBlock;
			var toName = Formatter.Description(block);

			if(fat == null || !fat.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(toName)} does not have an inventory.";
				return;
			}

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			fromList.Add(character.GetInventory());

			for (int ii = 0; ii < fat.InventoryCount; ++ii)
				toList.Add(fat.GetInventory(ii));

			InventoryTransfer(fromList, toList, "your inventory", toName, item, (MyFixedPoint)count, out commandResult);
		}

		internal void Transfer()
		{
			if(!GridIsSet()) return;

			double count; Vector3I ijkFrom, ijkTo;

			if(!tokenParser.NextDouble(out count)) { commandResult = "Error: expected count"; return; }

			var item = tokenParser.NextString();

			if(!tokenParser.Match("from")) { commandResult = "Error: expected 'from'"; return; }

			if(!tokenParser.NextVector3I(out ijkFrom)) { commandResult = "Error: expected I J K"; return; }

			if(!tokenParser.Match("to")) { commandResult = "Error: expected 'to'"; return; }

			if(!tokenParser.NextVector3I(out ijkTo)) { commandResult = "Error: expected I J K"; return; }

			var blockFrom = selectedGrid.GetCubeBlock(ijkFrom);
			if(blockFrom == null) { commandResult = $"Error: no block at {ijkFrom}"; return; }

			var blockTo = selectedGrid.GetCubeBlock(ijkTo);
			if(blockTo == null) { commandResult = $"Error: no block at {ijkTo}"; return; }

			var fatFrom = blockFrom.FatBlock;
			var fromName = Formatter.Description(blockFrom);

			if(fatFrom == null || !fatFrom.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(fromName)} does not have an inventory.";
				return;
			}

			var fatTo = blockTo.FatBlock;
			var toName = Formatter.Description(blockTo);

			if(fatTo == null || !fatTo.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(toName)} does not have an inventory.";
				return;
			}

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			for (int ii = 0; ii < fatFrom.InventoryCount; ++ii)
				fromList.Add(fatFrom.GetInventory(ii));

			for (int ii = 0; ii < fatTo.InventoryCount; ++ii)
				toList.Add(fatTo.GetInventory(ii));

			InventoryTransfer(fromList, toList, fromName, toName, item, (MyFixedPoint)count, out commandResult);
		}

		internal static void InventoryTransfer(List<IMyInventory> fromList, List<WTF_IMyInventory> toList,
			string fromName, string toName, string itemName, MyFixedPoint amount, out string result)
		{	
			List<MyInventoryItem> items = new List<MyInventoryItem>();

			IMyInventory from = null;
			int fromIndex = -1;

			for(int f = 0; f < fromList.Count; ++f)
			{
				from = fromList[f];

				items.Clear();
				from.GetItems(items);

				for (int i = 0; i < items.Count; i++)
				{
					var def = (MyDefinitionId)items[i].Type;
					var itemDef = MyDefinitionManager.Static.GetDefinition(def) as MyPhysicalItemDefinition;

					if (itemDef != null && itemDef.DisplayNameText == itemName)
					{
						fromIndex = i;
						break;
					}
				}

				if(fromIndex >= 0) break;
			}

			if (fromIndex < 0)
			{	result = $"Item {Formatter.Quote(itemName)} not found in inventory";
				return;
			}

			var item = items[fromIndex];

			if(item.Amount < amount)
			{	amount = item.Amount;
				// Emit warning?
			}

			foreach (var to in toList)
			{
				if (to.CanItemsBeAdded(amount, item.Type))
				{
					((WTF_IMyInventory)from).TransferItemTo(to, fromIndex, null, true, amount, false);
					result = $"Transferred {amount} {Formatter.Quote(itemName)} from {Formatter.Quote(fromName)} into {Formatter.Quote(toName)}";
					return;
				}
			}

			result = $"Cannot transfer {Formatter.Quote(itemName)} into {Formatter.Quote(toName)}";
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

		internal void Execute(string command)
		{
			var tp = new TokenParser(command);
			tokenParser = tp;
			commandResult = null;

			MyConsole.Add($"command: '{command}'", Color.Cyan);

			if(tp.Match("Help"))
			{	Help();
			}
			else if(tp.Match("Overview"))
			{	Overview();
			}
			else if(tp.Match("Search"))
			{	Search();
			}
			else if(tp.Match("Select_Asteroid"))
			{	Select(ObjectType.Asteroid);
			}
			else if(tp.Match("Select_grid") || tp.Match("Select"))
			{	Select(ObjectType.LargeShip);
			}
			else if(tp.Match("Fly"))
			{	Fly();
			}
			else if(tp.Match("Grind"))
			{	Grind();
			}
			else if(tp.Match("Weld"))
			{	Weld();
			}
			else if(tp.Match("Near"))
			{	Near();
			}
			else if(tp.Match("Inventory"))
			{	Inventory();
			}
			else if(tp.Match("Get"))
			{	Get();
			}
			else if(tp.Match("Put"))
			{	Put();
			}
			else if(tp.Match("Transfer"))
			{	Transfer();
			}
			else
			{	commandResult = $"Unknown command '{tp.NextString()}' use `help` to list all available commands.";
			}
			MyConsole.AddMultiline(commandResult);
		}
	}
}
