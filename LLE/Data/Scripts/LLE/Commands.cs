using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
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
		private const string IE_NO_INVENTORY = "Internal error: character.GetInventory() is null";

		private IMyCubeGrid selectedGrid;
		private MyVoxelBase selectedAsteroid;

		private readonly IMyCharacter character;
		
		internal Navigation navigation;

		private readonly StringBuilder tmp = new StringBuilder();

		private IEnumerator currentCommand;

		private MyEntity3DSoundEmitter soundEmitter;
		private MyParticleEffect particleEffect;

		private void EnableEffects(IMySlimBlock block, string particleName, string sound)
		{
			if (soundEmitter == null)
			{
				soundEmitter = new MyEntity3DSoundEmitter(character as MyEntity);
			}
			if (soundEmitter != null)
			{
				soundEmitter.VolumeMultiplier = Constants.SoundVolume;
				soundEmitter.PlaySound(new MySoundPair(sound));
			}
			if (particleEffect == null)
			{
				MatrixD m = MatrixD.Identity;
				Vector3D pos = Vector3D.Zero;
				if (MyParticlesManager.TryCreateParticleEffect(particleName, ref m, ref pos, uint.MaxValue,
					out particleEffect))
					particleEffect.UserRadiusMultiplier = 4f;
			}
			if (particleEffect != null)
			{
				BoundingBoxD box;
				block.GetWorldBoundingBox(out box, false);
				particleEffect.WorldMatrix = box.Matrix;
			}
		}

		internal void DisableEffects()
		{	if(soundEmitter != null)
			{
				soundEmitter.StopSound(false);
				soundEmitter = null;
			}
			if(particleEffect != null)
			{
				particleEffect.Stop();
				particleEffect = null;
			}
		}

		private double resumeTime;

		private void SetPause(double time)
		{	resumeTime = Time.Now + time;
		}
		private bool IsPaused()
		{	return Time.Now < resumeTime;			
		}

		public Commands(IMyCharacter character_)
		{	character = character_;
			navigation = new Navigation(character);
		}

		internal string Help()
		{
			return @"## AVAILABLE COMMANDS

* select_grid 'name'    		- Select a ship or station on which to grind, weld, and perform other operations.
* select_asteroid 'name'		- Select an asteroid on which to mine.

* overview						- List grid blocks by category.
* fly I J K						- Fly to specific grid coordinates. e.g. `fly 10 -5 13`
* grind I J K					- Grind a block at specific coordinates.
* weld I J K					- Weld a block at specific coordinates.
* near							- Return 6 accessible blocks around you and the block you are standing on.
* near I J K					- Return 6 accessible blocks around a block at specific coordinates.
* inventory						- Return the items in your inventory.
* inventory I J K				- Return the inventory of the container at specific coordinates.
* get count 'item' from I J K	- Transfer an item from a container to your inventory. e.g. `get 10 'Gold Ingot' from -1 5 2`
* put count 'item' into I J K	- Transfer an item from your inventory to a container. e.g. `put 1 'Medkit' into 14 0 2`
* transfer count 'item' from I1 J1 K1 to I2 J2 K2
								- Transfer an item from one inventory to another.
";
}

/*
* put all components into I J K	- Transfer all blocks components from your inventory to a container (very useful shortcut).
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

		internal string Overview()
		{
			string message;
			if(!GridIsSet(out message)) return message;

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

			return MyMarkdown.Result();
		}

		internal string Select(ObjectType type, TokenParser tp)
		{
			var what = tp.NextString();

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

			if(matches.Count != 1) return MyError(engineer, what, matches);

			var select = matches[0];

			Formatter.Description(select, out category, out name);

			switch(type)
			{	case ObjectType.LargeShip:
					selectedGrid = select as IMyCubeGrid;
					if(selectedGrid == null) return $"Error: {Formatter.Quote(name)} is {category}";
					break;
				case ObjectType.Asteroid:
					selectedAsteroid = select as MyVoxelBase;
					if(selectedAsteroid == null) return $"Error: {Formatter.Quote(name)} is {category}";
					break;
				default:
					return "Internal error";
			}

			return $"Selected {category} {Formatter.Quote(name)}";
		}

		internal bool GridIsSet(out string message)
		{	if(selectedGrid == null)
			{	message = "Error: you should select a grid first. Use `select_grid name`";
				return false;
			}
			message = null;
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

		internal IEnumerator Fly(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(!Collisions.CenterIsFree(block))
			{	
				Debug.Start(selectedGrid);

				tmp.Clear();
				tmp.Append($"Destination is blocked by {Formatter.Quote(Name(block))}, nearest free space is:\n");

				ListFreeSpace_ToTmp(ijk);

				yield return tmp.ToString();
			}

			navigation.FlyInsideGrid(selectedGrid, ijk);

			for(;;)
			{	yield return navigation.Step();				
			}
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

		internal bool IsTooFar(Vector3I ijk, out string message)
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
				message = tmp.ToString();
				return true;
			}
			message = null;
			return false;
		}

		internal IEnumerator Grind(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {Formatter.IJK(ijk)}";

			if(IsTooFar(ijk, out message)) yield return message;

			if(!EquipTool("Grinder"))
				yield return  "Cannot equip grinder. Do you have a grinder in your inventory?";

			var inventory = character.GetInventory();
			if (inventory == null) yield return IE_NO_INVENTORY;

			var equippedTool = character.EquippedTool as IMyAngleGrinder;
			if (equippedTool == null) yield return "Error: You should take an angle grinder.";

			float speedMultiplier = 1.0f;

			var item = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var itemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id);
			var toolBaseDef = itemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float grindAmount = Constants.WeldAndGrindSpeed * speedMultiplier * MyAPIGateway.Session.GrinderSpeedMultiplier;

			Vector3D bp;
			block.ComputeWorldCenter(out bp);

			SetPause(1.0);
			while (IsPaused())
			{
				navigation.JustRotateTo(bp);
				yield return null;
			}

			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {Formatter.IJK(ijk)}";

			// Apply grinding
			
			EnableEffects(block, MyParticleEffectsNameEnum.ShipGrinder, "ToolPlayGrindMetal");

			for(;;)
			{	
				// Check if inventory can accept at least one unit of any stockpile component
				Dictionary<string, int> stock = new Dictionary<string, int>();
				GetStockpileComponents(block, stock);

				var def = block.BlockDefinition as MyCubeBlockDefinition;
				bool canAccept = false;
				foreach (var c in def.Components)
				{	var k = c.Definition.Id.SubtypeName;
				
					if(stock[k] == 0) continue;

					if(inventory.CanItemsBeAdded(1, c.Definition.Id))
					{	canAccept = true;
						break;
					}
				}
				if (!canAccept)
				{	DisableEffects();
					yield return "Your inventory is full.";
				}

				block.DecreaseMountLevel(grindAmount, inventory);
				block.MoveItemsFromConstructionStockpile(inventory);

				// Handle block destruction
				if (block.IsDestroyed && block.StockpileEmpty)
				{
					block.SpawnConstructionStockpile();
					block.CubeGrid.RazeBlock(block.Min);

					DisableEffects();
					yield return $"Done! {Commands.Name(block)} is removed.";
				}

				yield return null;
			}
		}

		internal static void GetStockpileComponents(IMySlimBlock block, Dictionary<string, int> components)
		{
			components.Clear();

			var def = block.BlockDefinition as MyCubeBlockDefinition;
			
			foreach (var c in def.Components)
			{	var k = c.Definition.Id.SubtypeName;
				if(components.ContainsKey(k))
					components[k] += c.Count;
				else
					components[k] = c.Count;
			}
			
			Dictionary<string, int> missing = new Dictionary<string, int>();
			block.GetMissingComponents(missing);

			foreach (var kv in missing) components[kv.Key] -= kv.Value;
		}

		internal IEnumerator Weld(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if (block == null) yield return $"Error: no block at {Formatter.IJK(ijk)}";

			if (block.Integrity >= block.MaxIntegrity)
				yield return "The block is fully intact, no repairs needed.";

			if (IsTooFar(ijk, out message)) yield return message;

			if (!EquipTool("Welder"))
				yield return "Cannot equip welder. Do you have a welder in your inventory?";

			var inventory = character.GetInventory();
			if (inventory == null) yield return IE_NO_INVENTORY;

			var equippedTool = character.EquippedTool as IMyWelder;
			if (equippedTool == null) yield return "Error: You should take a welder.";

			float speedMultiplier = 1.0f;

			var item = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var itemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id);
			var toolBaseDef = itemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float weldAmount = Constants.WeldAndGrindSpeed * speedMultiplier * MyAPIGateway.Session.WelderSpeedMultiplier;

			// Check if block can accept components from inventory
			if (!block.CanContinueBuild(inventory)) yield return "You need components."; // XXX What components

			Vector3D bp;
			block.ComputeWorldCenter(out bp);

			SetPause(1.0);
			while (IsPaused())
			{
				navigation.JustRotateTo(bp);
				yield return null;
			}

			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {Formatter.IJK(ijk)}";

			// Apply welding

			EnableEffects(block, MyParticleEffectsNameEnum.WelderContactPoint, "ToolPlayWeldMetal");

			for (;;)
			{
				block.MoveItemsToConstructionStockpile(inventory);

				var pbi = block.Integrity;

				block.IncreaseMountLevel(weldAmount, character.ControllerInfo.ControllingIdentityId, inventory, 1.0f);

				if (block.Integrity >= block.MaxIntegrity)
				{
					DisableEffects();
					yield return "Done! Block integrity is full now.";
				}
				else if (block.Integrity == pbi)
				{
					DisableEffects();
					yield return "You need components."; // XXX What components
				}

				yield return null;
			}
		}

		internal string Near(TokenParser tp)
		{	
			string message;
			if(!GridIsSet(out message)) return message;

			MyMarkdown.Clear();

			Vector3I ijk;
			string hint;

			if(tp.End)
			{	var center = Utilities.GetEngineerCenter(character);
				ijk = selectedGrid.WorldToGridInteger(center);
				hint = "Your block";
			}
			else
			{	if(!tp.NextVector3I(out ijk)) return "Expected: I J K";
				hint = "Central block";
			}

			var name = Name(selectedGrid.GetCubeBlock(ijk));
			
			string firstLine = $"# {hint}: {Formatter.Quote(name)} Position: ({Formatter.IJK(ijk)})";

			positions.Clear();

			foreach (var direction in Constants.SixDirections)
			{	positions.Add(ijk + direction);
			}

			ListDescrtiption(positions, firstLine, false);

			return MyMarkdown.Result();
		}

		internal string Inventory(TokenParser tp)
		{
			if(tp.End)
			{
				var inv = character.GetInventory() as IMyInventory;
				if (inv == null) return IE_NO_INVENTORY;

				tmp.Clear();
				tmp.Append($"Your inventory:\n");
				InventoryToText(inv, tmp);

				return tmp.ToString();
			}
			else
			{	string message;

				if(!GridIsSet(out message)) return message;

				Vector3I ijk;
				if(!tp.NextVector3I(out ijk)) return "Error: expected I J K";

				var block = selectedGrid.GetCubeBlock(ijk);
				if(block == null) return $"Error: no block at {Formatter.IJK(ijk)}";

				var name = Name(block);
				var fat = block.FatBlock;

				if(fat == null || !fat.HasInventory)
					return $"Block {Formatter.Quote(name)} does not have an inventory.";

				tmp.Clear();
				var es = fat.InventoryCount == 1 ? "" : "es";
				tmp.Append($"Current inventory{es} of {Formatter.Quote(name)} (at {Formatter.IJK(ijk)}):\n");

				for(int i = 0; i < fat.InventoryCount; ++i)
				{	var inv = fat.GetInventory(i);
					InventoryToText(inv, tmp);
				}

				return tmp.ToString();
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

			foreach (var item in items)
			{
				var def = (MyDefinitionId)item.Type;
				var itemDef = MyDefinitionManager.Static.GetDefinition(def) as MyPhysicalItemDefinition;

				var volume = (double)item.Amount * itemDef.Volume;

				output.Append($"* {itemDef.DisplayNameText} → {item.Amount} ({Formatter.Volume(volume)})\n");
			}
		}

		internal IEnumerator Get(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			double count; Vector3I ijk;

			if(!tp.NextDouble(out count)) yield return "Error: expected count";

			var item = tp.NextString();

			if(!tp.Match("from")) yield return "Error: expected 'from'";

			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {Formatter.IJK(ijk)}";

			var fat = block.FatBlock;
			var fromName = Name(block);

			if(fat == null || !fat.HasInventory)
				yield return  $"Block {Formatter.Quote(fromName)} does not have an inventory.";

			if(IsTooFar(ijk, out message)) yield return message;

			Vector3D bp;
			block.ComputeWorldCenter(out bp);

			SetPause(2.0);
			while(IsPaused())
			{	navigation.JustRotateTo(bp);
				yield return null;
			}

			// recheck
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {Formatter.IJK(ijk)}";

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			for (int ii = 0; ii < fat.InventoryCount; ++ii)
				fromList.Add(fat.GetInventory(ii));

			toList.Add(character.GetInventory());

			InventoryTransfer(fromList, toList, fromName, "your inventory", item, (MyFixedPoint)count, out message);
			yield return message;
		}

		internal IEnumerator Put(TokenParser tp)
		{	
			string message;
			if(!GridIsSet(out message)) yield return message;

			double count; Vector3I ijk;

			if(!tp.NextDouble(out count)) yield return "Error: expected count";

			var item = tp.NextString();

			if(!tp.Match("into")) yield return "Error: expected 'into'";

			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {Formatter.IJK(ijk)}";

			var inv = character.GetInventory() as IMyInventory;
			if (inv == null) yield return IE_NO_INVENTORY;

			var fat = block.FatBlock;
			var toName = Name(block);

			if(fat == null || !fat.HasInventory)
				yield return $"Block {Formatter.Quote(toName)} does not have an inventory.";

			if(IsTooFar(ijk, out message)) yield return message;

			Vector3D bp;
			block.ComputeWorldCenter(out bp);

			SetPause(2.0);
			while(IsPaused())
			{	navigation.JustRotateTo(bp);
				yield return null;
			}

			// recheck
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {Formatter.IJK(ijk)}";

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			fromList.Add(character.GetInventory());

			for (int ii = 0; ii < fat.InventoryCount; ++ii)
				toList.Add(fat.GetInventory(ii));

			InventoryTransfer(fromList, toList, "your inventory", toName, item, (MyFixedPoint)count, out message);
			yield return message;
		}

		internal IEnumerator Transfer(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			double count; Vector3I ijkFrom, ijkTo;

			if(!tp.NextDouble(out count)) yield return "Error: expected count";

			var item = tp.NextString();

			if(!tp.Match("from")) yield return "Error: expected 'from'";

			if(!tp.NextVector3I(out ijkFrom)) yield return "Error: expected I J K";

			if(!tp.Match("to")) yield return "Error: expected 'to'";

			if(!tp.NextVector3I(out ijkTo)) yield return "Error: expected I J K";

			var blockFrom = selectedGrid.GetCubeBlock(ijkFrom);
			if(blockFrom == null) yield return $"Error: no block at {Formatter.IJK(ijkFrom)}";

			var blockTo = selectedGrid.GetCubeBlock(ijkTo);
			if(blockTo == null) yield return  $"Error: no block at {Formatter.IJK(ijkTo)}";

			var fatFrom = blockFrom.FatBlock;
			var fromName = Name(blockFrom);

			if(fatFrom == null || !fatFrom.HasInventory)
				yield return $"Block {Formatter.Quote(fromName)} does not have an inventory.";

			var fatTo = blockTo.FatBlock;
			var toName = Name(blockTo);

			if(fatTo == null || !fatTo.HasInventory)
				yield return $"Block {Formatter.Quote(toName)} does not have an inventory.";

			if(IsTooFar(ijkFrom, out message) && IsTooFar(ijkTo, out message)) yield return message; /// XXX Incorrect!

			List<IMyInventory> fromList = new List<IMyInventory>();
			List<WTF_IMyInventory> toList = new List<WTF_IMyInventory>();

			for (int ii = 0; ii < fatFrom.InventoryCount; ++ii)
				fromList.Add(fatFrom.GetInventory(ii));

			for (int ii = 0; ii < fatTo.InventoryCount; ++ii)
				toList.Add(fatTo.GetInventory(ii));

			InventoryTransfer(fromList, toList, fromName, toName, item, (MyFixedPoint)count, out message);
			yield return message;
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
		{	return currentCommand != null;
		}

		internal string Update()
		{
			if (currentCommand == null) return null;

			// yield return null; = wait
			// yield retrurn string; = response to LLM
			// yield break; = no respone, done
			// ! Через yield return null не переносим ссылки на engine-объекты, которые можно заново найти.

			//MyConsole.AddMultiline(".", Color.AliceBlue);
				
			if (currentCommand.MoveNext())
			{	
				//MyConsole.AddMultiline("M", Color.AliceBlue);
					
				var result = currentCommand.Current as string;

				if(result != null)
				{	MyConsole.Add($"result {result}");

					(currentCommand as IDisposable)?.Dispose();
					currentCommand = null;
					return result;
				}
			}
			else
			{	MyConsole.Add("!yield break!", Color.DarkRed);
				(currentCommand as IDisposable)?.Dispose();
				currentCommand = null;
			}
			return null;
		}

		internal string Execute(string command)
		{
			string result = null;

			var tp = new TokenParser(command);

			if(tp.Match("Overview"))
			{	result = Overview();
			}
			else if(tp.Match("Select_Asteroid"))
			{	Select(ObjectType.Asteroid, tp);
			}
			else if(tp.Match("Select_grid") || tp.Match("Select"))
			{	Select(ObjectType.LargeShip, tp);
			}
			else if(tp.Match("Fly"))
			{	currentCommand = Fly(tp);
			}
			else if(tp.Match("Grind"))
			{	currentCommand = Grind(tp);
			}
			else if(tp.Match("Weld"))
			{	currentCommand = Weld(tp);
			}
			else if(tp.Match("Near"))
			{	result = Near(tp);
			}
			else if(tp.Match("Inventory"))
			{	result = Inventory(tp);
			}
			else if(tp.Match("Get"))
			{	currentCommand = Get(tp);
			}
			else if(tp.Match("Put"))
			{	currentCommand = Put(tp);
			}
			else if(tp.Match("Transfer"))
			{	currentCommand = Transfer(tp);
			}
			else
			{	result = $"Unknown command '{tp.NextString()}'.";
			}

			return result;
		}
	}
}
