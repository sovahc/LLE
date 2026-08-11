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
		internal bool FindTool(string toolSubtype, out MyDefinitionId id)
		{
			id = new MyDefinitionId();

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

			id = targetDefId.Value;
			return true;
		}

		internal void EquipTool(MyDefinitionId id)
		{
			var controller = character as Sandbox.Game.Entities.IMyControllableEntity;
			controller.SwitchToWeapon(id);
		}

		internal IEnumerator Grind(ToolCall call)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!call.Ijk(out ijk)) yield return call.NeedIjk;

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			MyDefinitionId grinder;
			if(!FindTool("Grinder", out grinder))
				yield return "Cannot equip grinder. Do you have a Grinder in your inventory?";

			var ip = GetInteractionPointAt(block, InteractionKind.GrindWeld, GetEngineerCenter());
			if(!ip.HasValue)
				yield return E_BAD_POSITION;

			var position = ip.Value.chPosition;
			var target = ip.Value.Target;

			yield return Validated;

			EquipTool(grinder);

			// TODO: warning on WillRemoveBlockSplitGrid

			var grinderGun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
			if(grinderGun == null) yield return "Internal error: equipped tool is not IMyGunObject<MyDeviceBase>";

			SetPause(Constants.MicronavigationDelay);
			while(IsPaused())
			{
				CharacterMove(position);
				yield return null;
			}

			SetPause(Constants.MicronavigationDelay);
			while(IsPaused())
			{
				CharacterRotateTo(target);
				yield return null;
			}

			// check if block still exists
			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			var integrity0 = block.Integrity;

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
				{	grinderGun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
					if(grinderGun == null)
						yield return Incomplete("Grinder was unequipped — grinding stopped.");
					
					if(grinderGun.CanShoot(MyShootActionEnum.PrimaryAction, character.EntityId, out status))
						break;
					
					yield return null;
				}

				if(status != MyGunStatusEnum.OK)
				{	grinderGun.EndShoot(MyShootActionEnum.PrimaryAction);
					yield return $"Tool status: {status}";
				}

				var myInv = inventory as MyInventory;
				if(myInv == null) yield return "Internal error: inventory is not MyInventory";

				bool inventoryFull = false;

				GetStockpileComponents(block, stockpile);

				var def = block.BlockDefinition as MyCubeBlockDefinition;
				foreach (var c in def.Components)
				{	var k = c.Definition.Id.SubtypeName;

					if (stockpile[k] == 0) continue;

					if (myInv.ComputeAmountThatFits(c.Definition.Id) > 0) continue;

					inventoryFull = true;
					break;
				}

				var pbi = block.Integrity;

				// Native grinding: Shoot activates the tool (spinning disc, sound, particles)
				// and after preheat the tool grinds the raycast-hit block itself.
				if(!inventoryFull)
					grinderGun.Shoot(MyShootActionEnum.PrimaryAction, (Vector3)character.WorldMatrix.Forward, null);

				bool stale = pbi == block.Integrity;
				bool removed = block.IsDestroyed && block.StockpileEmpty;

				if (inventoryFull || stale || removed)
				{	
					grinderGun.EndShoot(MyShootActionEnum.PrimaryAction);
					
					StringBuilder sb = new StringBuilder();
					if(!removed)
					{	var p0 = integrity0 / block.MaxIntegrity;
						var p1 = block.Integrity / block.MaxIntegrity;
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

					if(removed)
					{	block.SpawnConstructionStockpile();
						block.CubeGrid.RazeBlock(block.Min);
					}

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

			var block = BlockAt(ijk);
			if (block == null) yield return $"Error: no block at {IJK(ijk)}";

			bool projection = IsProjection(block.CubeGrid);

			if (block.Integrity >= block.MaxIntegrity && !projection)
				yield return Success("The block is fully intact; no repairs needed.");

			MyDefinitionId welder;
			if (!FindTool("Welder", out welder))
				yield return "Cannot equip handheld welder. Do you have a Welder in your inventory?";

			var ip = GetInteractionPointAt(block, InteractionKind.GrindWeld, GetEngineerCenter());
			if(!ip.HasValue)
				yield return E_BAD_POSITION;

			var position = ip.Value.chPosition;
			var target = ip.Value.Target;

			yield return Validated;

			EquipTool(welder);

			var welderGun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
			if(welderGun == null) yield return "Internal error: equipped tool is not IMyGunObject<MyDeviceBase>";

			SetPause(Constants.MicronavigationDelay);
			while(IsPaused())
			{
				CharacterMove(position);
				yield return null;
			}

			SetPause(Constants.MicronavigationDelay);
			while(IsPaused())
			{
				CharacterRotateTo(target);
				yield return null;
			}

			// check if block still exists
			block = BlockAt(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			var inventory = character.GetInventory();
			if (inventory == null) yield return IE_NO_INVENTORY;

			StringBuilder result = new StringBuilder();

			if(projection)
			{	
				var projector = ProjectorOf(block.CubeGrid);
				if(projector == null)
					yield return "Error: could not find the projector owning this projection.";

				var realGrid = projector.CubeGrid;
				var worldPos = block.CubeGrid.GridIntegerToWorld(block.Position);
				var realCell = realGrid.WorldToGridInteger(worldPos);
				var realBlock = realGrid.GetCubeBlock(realCell);

				if(realBlock == null)
				{
					// The projector places the frame itself. A welder shot would have to reach the ghost,
					// and its raycast disagrees with ours often enough to refuse a good position.
					var owner = character.ControllerInfo.ControllingIdentityId;
					projector.Build(block, owner, character.EntityId, true, owner);

					SetPause(Constants.MicronavigationDelay);
					while(IsPaused())
					{	realBlock = realGrid.GetCubeBlock(realCell);
						if(realBlock != null) break;
						yield return null;
					}

					if(realBlock == null)
						yield return $"Error: the projector did not place {Quote(Name(block))} at {IJK(realCell)}.";

					// switch to real block
					block = realBlock;
					result.Append($"Block placed on grid '{realGrid.DisplayName}' at {IJK(realCell)}\n");
				}
			}

			if (!block.CanContinueBuild(inventory))
			{	
				result.Append("You don't have the required components in your inventory\n");
				AddMissingComponentsString(block, result);
				
				yield return Incomplete(result.ToString());
			}

			var current = new Dictionary<string, double>();
			InventoryDelta(inventory, current, +1);

			var integrity0 = block.Integrity;

			for (;;)
			{
				MyGunStatusEnum status = MyGunStatusEnum.Cooldown;
				for(int i = 0; i < 30; ++i)
				{	welderGun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
					if(welderGun == null)
						yield return Incomplete("Welder was unequipped — welding stopped.");
					
					if(welderGun.CanShoot(MyShootActionEnum.PrimaryAction, character.EntityId, out status))
						break;
					
					yield return null;
				}

				if(status != MyGunStatusEnum.OK)
				{	welderGun.EndShoot(MyShootActionEnum.PrimaryAction);
					yield return $"Tool status: {status}";
				}

				block.MoveItemsToConstructionStockpile(inventory);

				var pbi = block.Integrity;

				welderGun.Shoot(MyShootActionEnum.PrimaryAction, (Vector3)character.WorldMatrix.Forward, null);

				bool stale = pbi == block.Integrity;
				bool full = block.Integrity >= block.MaxIntegrity;

				if (stale || full)
				{	
					welderGun.EndShoot(MyShootActionEnum.PrimaryAction);

					if(full) result.Append("Done! Block integrity is full.");
					else
					{	var p0 = integrity0 / block.MaxIntegrity;
						var p1 = block.Integrity / block.MaxIntegrity;
						result.Append($"Block integrity changed from {Percent(p0)} to {Percent(p1)}\n");

						if(stale) AddMissingComponentsString(block, result);
						if(integrity0 == block.Integrity)
							result.Append("If this repeats, try a different interaction point.\n");
					}

					yield return full ? Success(result.ToString()) : Incomplete(result.ToString());
				}

				yield return null;
			}
		}
	}
}
