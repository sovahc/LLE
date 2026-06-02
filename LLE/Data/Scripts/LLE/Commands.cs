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
		{	if (s == null) return "(null)";
			if (!s.Contains(' ')) return s;
			return $"'{s}'";
		}

		public static string Distance(double d)
		{	if (d < 1000)
				return $"{(int)Math.Round(d, 0, MidpointRounding.AwayFromZero)}m";
			return $"{d / 1000.0:F1}km";
		}

		public static string Percent(float f)
		{	var ff = (int)Math.Round(f * 100, 0, MidpointRounding.AwayFromZero);
			return $"{ff}%";
		}

		public static string Volume(double d)
		{	var dd = Math.Round(d, 2, MidpointRounding.AwayFromZero);
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
		
		internal Navigation navigation;
		private BotTools botTools;

		private readonly StringBuilder tmp = new StringBuilder();

		internal TokenParser tokenParser;
		public string commandResult;

		public Commands(IMyCharacter character_)
		{	character = character_;
			botTools = new BotTools(character);
			navigation = new Navigation(character);
		}

		internal string Help()
		{
			return @"## AVAILABLE COMMANDS

* select_grid 'name'    		- Select a ship or station on which to grind, weld, and perform other operations.
* select_asteroid 'name'		- Select an asteroid on which to mine.

* overview						- List grid blocks by category.
* fly I J K						- Fly to specific grid coordinates (integer values)
* grind I J K					- Grind a block at specific coordinates.
* weld I J K					- Weld a block at specific coordinates.
* near							- Return 6 accessible blocks around you and the block you are standing on.
* near I J K				- Return 6 accessible blocks around a block at specific coordinates.
* inventory						- Return the items in your inventory.
* inventory I J K				- Return the inventory of the container at specific coordinates.
* get count 'item' from I J K	- Transfer an item from a container to your inventory. e.g. `get 10 'Gold Ingot' from -1 5 2`
* put count 'item' into I J K	- Transfer an item from your inventory to a container. e.g. `put 1 'Medkit' into 14 0 2`
* transfer count 'item' from I1 J1 K1 to I2 J2 K2
								- Transfer an item from one inventory to another.
";
}

/*
* search 'substring'			- Search block coordinates by name.
search ['substring']   - Find any objects by partial match. Ex: `search` (search anything), `search STATION`, `search Steel Plate`
vision                 - Get current visual input (what the bot sees right now)
info 'name'        - Get detailed information about a specific object.
look at 'name'     - Rotate to face the object
hack 'block_name'  - Grind a specific block just below the hacking point (weld it back to restore functionality).
mine 'block_name'  - Mine a specific ore deposit.
status             - Check bot status: Battery, Hydrogen, Oxygen.
pickup 'name'      - Pick up a specified object.
drop 'name' [quantity|all] - Drop a specified object.
? move {forward|backward|left|right|up|down} {distance} - Move in a direction
? recover from being stuck
? save to memory 'string'
! Pathfinding: safest (default) / shortest / scouting / prefer open space

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

		private static readonly List<IMyTerminalBlock> terminalBlocks = new List<IMyTerminalBlock>();
		private static readonly List<IMySlimBlock> slimBlocks = new List<IMySlimBlock>();

		private static readonly Dictionary<string, List<Vector3I>> describer = new Dictionary<string, List<Vector3I>>();
		private static readonly List<Vector3I> positions = new List<Vector3I>();

		internal static string NameToCategory(string name)
		{	foreach (var cat in TerminalBCategories)
			{
				if (cat.Value.Any(keyword => name.Contains(keyword)))
				{
					return cat.Key;
				}
			}
			return "Other";
		}

		public static string Name(IMySlimBlock block)
		{
			if(block == null) return "Free space";
			return block.BlockDefinition.DisplayNameText;
		}

		internal void ListDescrtiption(List<Vector3I> coordinates, string firstLine, bool byCategory)
		{	MyMarkdown.Clear();
			MyMarkdown.Append(firstLine);
			MyMarkdown.Append($"Legend: Name → count (positions on the grid)");

			describer.Clear();

			foreach (var position in coordinates)
			{	
				var name = Name(selectedGrid.GetCubeBlock(position));

				List<Vector3I> pp;
				if(!describer.TryGetValue(name, out pp))
				{	pp = new List<Vector3I>();
					describer[name] = pp;
				}
				pp.Add(position);
			}

			foreach (var kv in describer)
			{	
				var name = kv.Key;
				var category = byCategory ? NameToCategory(name) : null;

				tmp.Clear();
				tmp.Append($"* {Formatter.Quote(kv.Key)} → {kv.Value.Count} (");

				bool semi = false;
				foreach(var p in kv.Value)
				{	if(semi) tmp.Append("; ");
					tmp.Append(Formatter.IJK(p));
					semi = true;
				}
				tmp.Append(")");

				if(byCategory)
					MyMarkdown.Add($"## {category}", tmp.ToString());
				else
					MyMarkdown.Append(tmp.ToString());
			}

			describer.Clear();
		}

		internal void Overview()
		{
			if(!GridIsSet()) return;

			string category, name;
			Formatter.Description(selectedGrid as MyEntity, out category, out name);

			string firstLine = $"# {category} '{name}'";

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(selectedGrid);
			terminalBlocks.Clear();
			ts.GetBlocks(terminalBlocks);

			positions.Clear();
			foreach(var block in terminalBlocks) positions.Add(block.SlimBlock.Position);

			ListDescrtiption(positions, firstLine, true);

			terminalBlocks.Clear();
			positions.Clear();

			commandResult = MyMarkdown.Result();
		}

		internal void Search()
		{
/*			if(!GridIsSet()) return;

			string query = tokenParser.NextString();
			if(query == "")
			{	commandResult = "Error: query cannot be empty";
				return;
			}

			string category, name;
			Formatter.Description(selectedGrid as MyEntity, out category, out name);

			MyMarkdown.Clear();
			MyMarkdown.Append($"# SEARCH ON {category} '{name}' QUERY: '{query}'");

			selectedGrid.GetBlocks(slimBlocks);

			foreach(var block in slimBlocks)
			{	
				Description(block, out category, out name);

				MyMarkdown.Add(category, name);

				if(Include(query, category) || Include(query, name))
				{	MyMarkdown.Add(name, Formatter.IJK(block.Position));
				}
			}

			slimBlocks.Clear();

			commandResult = MyMarkdown.Result();*/
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

		internal void ListFreeSpace_ToTmp(Vector3I ijk)
		{	
			tmp.Append("(");

			bool semi = false;
			foreach (var direction in Constants.SixDirections)
			{	var position = ijk + direction;
					
				var block = selectedGrid.GetCubeBlock(position);
				if(Collisions.CenterIsFree(block))
				{	if(semi) tmp.Append("; ");
					tmp.Append(Formatter.IJK(position));
					semi = true;
					Debug.highlightCellsGreen.Add(position);
				}
				else
					Debug.highlightCellsRed.Add(position);
			}
			if(!semi) // nothing added
			{	tmp.Append(" -- none -- ");
			}
			tmp.Append(")\n");
		}

		internal void Fly()
		{
			if(!GridIsSet()) return;

			Vector3I ijk;
			if(!tokenParser.NextVector3I(out ijk)) return;

			var block = selectedGrid.GetCubeBlock(ijk);
			if(!Collisions.CenterIsFree(block))
			{	
				Debug.Start(selectedGrid);

				tmp.Clear();
				tmp.Append($"Destination is blocked by {Formatter.Quote(Name(block))}, nearest free space is:\n");

				ListFreeSpace_ToTmp(ijk);

				commandResult = tmp.ToString();
				return;
			}

			navigation.FlyInsideGrid(selectedGrid, ijk);
			currentAction = Action.Flying;
		}

		internal bool EquipTool(string toolSubtype)
		{
			var inventory = character.GetInventory();
			if (inventory == null) return false;

			List<MyInventoryItem> items = new List<MyInventoryItem>();
			inventory.GetItems(items);

			MyDefinitionId? targetDefId = null;
			foreach (var item in items)
			{
				var handItemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Type);
				if (handItemDef != null && handItemDef.Id.SubtypeName.IndexOf(toolSubtype, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					targetDefId = handItemDef.PhysicalItemId;
					break;
				}
			}

			if (targetDefId == null) return false;

			var controller = character as Sandbox.Game.Entities.IMyControllableEntity;
			if (!controller.CanSwitchToWeapon(targetDefId.Value)) return false;
			
			controller.SwitchToWeapon(targetDefId.Value);
			return true;
		}

		internal bool IsTooFar(Vector3I ijk)
		{
			var block = selectedGrid.GetCubeBlock(ijk);

			Vector3D world;
			block.ComputeWorldCenter(out world);
			var distance = (world - Utilities.GetEngineerCenter(character)).Length();
			if(distance > 5)
			{	
				tmp.Clear();
				tmp.Append($"You are too far from {Name(block)} to interact ({Formatter.Distance(distance)})\n");
				tmp.Append($"Possible interaction points is: ");
				ListFreeSpace_ToTmp(ijk);
				commandResult = tmp.ToString();
				return true;
			}
			return false;
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
			{	commandResult = $"Error: no block at {Formatter.IJK(ijk)}";
				return;
			}

			if(IsTooFar(ijk)) return;

			if(!EquipTool("Grinder"))
			{	commandResult = "Cannot equip grinder. Do you have a grinder in your inventory?";
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

			if(block.Integrity >= block.MaxIntegrity)
			{	commandResult = "The block is fully intact, no repairs needed.";
				return;				
			}

			if(IsTooFar(ijk)) return;

			if(!EquipTool("Welder"))
			{	commandResult = "Cannot equip welder. Do you have a welder in your inventory?";
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

			Vector3I ijk;
			string hint;

			if(tokenParser.End)
			{	var center = Utilities.GetEngineerCenter(character);
				ijk = selectedGrid.WorldToGridInteger(center);
				hint = "Your block";
			}
			else
			{	if(!tokenParser.NextVector3I(out ijk)) return;
				hint = "Central block";
			}

			var name = Name(selectedGrid.GetCubeBlock(ijk));
			
			string firstLine = $"# {hint}: {Formatter.Quote(name)} Position: ({Formatter.IJK(ijk)})";

			positions.Clear();

			foreach (var direction in Constants.SixDirections)
			{	positions.Add(ijk + direction);
			}

			ListDescrtiption(positions, firstLine, false);

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

				var name = Name(block);
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

			if(inv.ItemCount == 0)
			{	output.Append("-- No items --\n");
				return;
			}

			List<MyInventoryItem> items = new List<MyInventoryItem>();
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
			var fromName = Name(block);

			if(fat == null || !fat.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(fromName)} does not have an inventory.";
				return;
			}

			if(IsTooFar(ijk)) return;

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
			var toName = Name(block);

			if(fat == null || !fat.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(toName)} does not have an inventory.";
				return;
			}

			if(IsTooFar(ijk)) return;

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
			var fromName = Name(blockFrom);

			if(fatFrom == null || !fatFrom.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(fromName)} does not have an inventory.";
				return;
			}

			var fatTo = blockTo.FatBlock;
			var toName = Name(blockTo);

			if(fatTo == null || !fatTo.HasInventory)
			{	commandResult = $"Block {Formatter.Quote(toName)} does not have an inventory.";
				return;
			}

			if(IsTooFar(ijkFrom) && IsTooFar(ijkTo)) return; /// XXX Incorrect!

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

		internal bool InProgress()
		{	return currentAction != Action.Idle;			
		}

		internal void Update()
		{
			switch(currentAction)
			{	case Action.Idle:
					return;
				case Action.Flying:
					commandResult = navigation.Step();
					if(commandResult != null)
						currentAction = Action.Idle;
					return;
				case Action.Grinding:
					commandResult = botTools.GrindBlock();
					if(commandResult != null)
					{	botTools.Stop();
						currentAction = Action.Idle;
					}
					return;
				case Action.Welding:
					commandResult = botTools.WeldBlock();
					if(commandResult != null)
					{	botTools.Stop();
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

			if(tp.Match("Overview"))
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
			{	commandResult = $"Unknown command '{tp.NextString()}'.";
			}
		}
	}
}
