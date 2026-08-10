using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game;

using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;

namespace LLE
{
	public partial class Commands
	{	
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
				if (handItemDef == null) continue;
				if (handItemDef.Id.SubtypeName.IndexOf(toolSubtype, StringComparison.OrdinalIgnoreCase) < 0) continue;
				
				targetDefId = handItemDef.PhysicalItemId;
				break;
			}

			if (targetDefId == null) return false;

			var controller = character as Sandbox.Game.Entities.IMyControllableEntity;
			if (!controller.CanSwitchToWeapon(targetDefId.Value)) return false;
			
			controller.SwitchToWeapon(targetDefId.Value);
			return true;
		}

		internal IEnumerator Grind(ToolCall call)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!call.Ijk(out ijk)) yield return call.NeedIjk;

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			if(!world.EquipTool("Grinder"))
				yield return "Cannot equip grinder. Do you have a Grinder in your inventory?";

			var ip = GetInteractionPointAt(block, InteractionKind.GrindWeld, GetEngineerCenter());
			if(!ip.HasValue)
				yield return E_BAD_POINT;
			
			var position = ip.Value.chPosition;
			var target = ip.Value.Target;

			// TODO: warning on WillRemoveBlockSplitGrid

			if(!world.ToolEquipped) yield return "Internal error: equipped tool is not IMyGunObject<MyDeviceBase>";

			world.SetPause(Constants.MicronavigationDelay);
			while(world.IsPaused())
			{
				world.Move(position);
				yield return null;
			}

			world.SetPause(Constants.MicronavigationDelay);
			while(world.IsPaused())
			{
				world.RotateTo(target);
				yield return null;
			}

			// check if block still exists
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {IJK(ijk)}";

			var integrity0 = world.Integrity(block);

			var inventory = character.GetInventory();
			if(inventory == null) yield return IE_NO_INVENTORY;

			var current = new Dictionary<string, double>();
			InventoryDelta(inventory, current, +1);

			Dictionary<string, int> stockpile = new Dictionary<string, int>();

			for(;;)
			{
				// CanShoot enforces ToolCooldownMs (250ms) so Grind doesn't fire every tick.
				MyGunStatusEnum status = MyGunStatusEnum.Cooldown;
				for(int i = 0; i < 30; ++i)
				{	if(!world.ToolEquipped)
						yield return Incomplete("Grinder was unequipped — grinding stopped.");

					if(world.ToolReady(out status))
						break;

					yield return null;
				}

				if(status != MyGunStatusEnum.OK)
				{	world.ToolStop();
					yield return $"Tool status: {status}";
				}

				bool inventoryFull = false;

				GetStockpileComponents(block, stockpile);

				var def = block.BlockDefinition as MyCubeBlockDefinition;
				foreach (var c in def.Components)
				{	var k = c.Definition.Id.SubtypeName;

					if (stockpile[k] == 0) continue;

					if (world.AmountThatFits(inventory, c.Definition.Id) > 0) continue;

					inventoryFull = true;
					break;
				}

				var pbi = world.Integrity(block);

				// Native grinding: Shoot activates the tool (spinning disc, sound, particles)
				// and after preheat the tool grinds the raycast-hit block itself.
				if(!inventoryFull)
					world.ToolShoot(block);

				bool stale = pbi == world.Integrity(block);
				bool removed = world.IsDestroyed(block) && world.StockpileEmpty(block);

				if (inventoryFull || stale || removed)
				{
					world.ToolStop();

					StringBuilder sb = new StringBuilder();
					if(!removed)
					{	var p0 = integrity0 / block.MaxIntegrity;
						var p1 = world.Integrity(block) / block.MaxIntegrity;
						sb.Append($"Block integrity changed from {Percent(p0)} to {Percent(p1)}\n");
					}
					sb.Append($"Inventory change:\n");

					InventoryDelta(inventory, current, -1);
					InventoryDiff(current, sb);

					if(inventoryFull) sb.Append("Your inventory is full.\n");
					else if(stale)
					{	sb.Append("No progress!\n");
						sb.Append("If this repeats, try a different interaction point.\n");
					}
					
					if(removed) sb.Append($"Done! {Name(block)} has been removed.");

					if(removed) world.RazeBlock(block);

					yield return removed ? Success(sb.ToString())
						: inventoryFull ? Incomplete(sb.ToString())
						: sb.ToString(); // stale — no progress, genuine error
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
	
		internal static void AddMissingComponentsString(IMySlimBlock block, StringBuilder result)
		{	Dictionary<string, int> missing = new Dictionary<string, int>();
			block.GetMissingComponents(missing);

			result.Append("Missing components: ");

			bool first = true;
			foreach (var kv in missing)
			{	var id = new MyDefinitionId(typeof(MyObjectBuilder_Component), kv.Key);
				var def = MyDefinitionManager.Static.GetDefinition(id) as MyPhysicalItemDefinition;
				if(def == null) continue;
				var name = def.DisplayNameText;
				if (!first) result.Append(", ");
				result.Append($"{kv.Value} {Quote(name)}");
				first = false;
			}
			if(first) result.Append("-- none --");
			result.Append("\n");
		}

		internal IEnumerator Weld(ToolCall call)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if (!call.Ijk(out ijk)) yield return call.NeedIjk;

			var block = selectedGrid.GetCubeBlock(ijk);
			if (block == null) yield return $"Error: no block at {IJK(ijk)}";

			bool projection = IsProjection(block.CubeGrid);

			if (world.Integrity(block) >= block.MaxIntegrity && !projection)
				yield return Success("The block is fully intact; no repairs needed.");

			if (!world.EquipTool("Welder"))
				yield return "Cannot equip handheld welder. Do you have a Welder in your inventory?";

			if(!world.ToolEquipped) yield return "Internal error: equipped tool is not IMyGunObject<MyDeviceBase>";

			var ip = GetInteractionPointAt(block, InteractionKind.GrindWeld, GetEngineerCenter());
			if(!ip.HasValue)
				yield return E_BAD_POINT;
			
			var position = ip.Value.chPosition;
			var target = ip.Value.Target;

			world.SetPause(Constants.MicronavigationDelay);
			while(world.IsPaused())
			{
				world.Move(position);
				yield return null;
			}

			world.SetPause(Constants.MicronavigationDelay);
			while(world.IsPaused())
			{
				world.RotateTo(target);
				yield return null;
			}

			// check if block still exists
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {IJK(ijk)}";

			var inventory = character.GetInventory();
			if (inventory == null) yield return IE_NO_INVENTORY;

			StringBuilder result = new StringBuilder();

			if(projection)
			{	
				var mcg = selectedGrid.Grid as MyCubeGrid;
				var projector = mcg.Projector as IMyProjector;
				if(projector == null)
					yield return "Error: could not find the projector owning this projection.";
				
				var builtGrid = projector.CubeGrid;
				var worldPos = selectedGrid.GridIntegerToWorld(block.Position);
				var builtBlock = builtGrid.GetCubeBlock(builtGrid.WorldToGridInteger(worldPos));

				if(builtBlock == null)
				{	if(!world.ToolEquipped) yield return "Internal error: equipped tool is not IMyGunObject<MyDeviceBase>";

					// place block
					world.ToolShoot(block);
					world.ToolStop();


					builtBlock = builtGrid.GetCubeBlock(builtGrid.WorldToGridInteger(worldPos));
					if(builtBlock == null)
						yield return "Error: can't place block using projection.";

					// switch to built block
					block = builtBlock;
					result.Append($"Block placed on grid '{builtGrid.DisplayName}'\n");
				}
			}

			if (!world.CanContinueBuild(block, inventory))
			{	
				result.Append("You don't have the required components in your inventory\n");
				AddMissingComponentsString(block, result);
				
				yield return Incomplete(result.ToString());
			}

			var current = new Dictionary<string, double>();
			InventoryDelta(inventory, current, +1);

			var integrity0 = world.Integrity(block);

			for (;;)
			{
				MyGunStatusEnum status = MyGunStatusEnum.Cooldown;
				for(int i = 0; i < 30; ++i)
				{	if(!world.ToolEquipped)
						yield return Incomplete("Welder was unequipped — welding stopped.");

					if(world.ToolReady(out status))
						break;

					yield return null;
				}

				if(status != MyGunStatusEnum.OK)
				{	world.ToolStop();
					yield return $"Tool status: {status}";
				}

				world.MoveItemsToConstructionStockpile(block, inventory);

				var pbi = world.Integrity(block);

				world.ToolShoot(block);

				bool stale = pbi == world.Integrity(block);
				bool full = world.Integrity(block) >= block.MaxIntegrity;

				if (stale || full)
				{
					world.ToolStop();

					if(full) result.Append("Done! Block integrity is full.");
					else
					{	var p0 = integrity0 / block.MaxIntegrity;
						var p1 = world.Integrity(block) / block.MaxIntegrity;
						result.Append($"Block integrity changed from {Percent(p0)} to {Percent(p1)}\n");

						if(stale) AddMissingComponentsString(block, result);
						if(integrity0 == world.Integrity(block))
							result.Append("If this repeats, try a different interaction point.\n");
					}

					yield return full ? Success(result.ToString()) : Incomplete(result.ToString());
				}

				yield return null;
			}
		}
	}
}
