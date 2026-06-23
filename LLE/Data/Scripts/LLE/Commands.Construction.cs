using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;

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
		
		internal IEnumerator Grind(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {IJK(ijk)}";

			if(IsTooFar(ijk, out message)) yield return message;

			if(!EquipTool("Grinder"))
				yield return  "Cannot equip grinder. Do you have a handheld angle grinder in your inventory?";

			var inventory = character.GetInventory();
			if (inventory == null) yield return IE_NO_INVENTORY;

			var equippedTool = character.EquippedTool as IMyAngleGrinder;
			if (equippedTool == null) yield return "Internal error: equippedTool is not IMyAngleGrinder";

			float speedMultiplier = 1.0f;

			var item = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var itemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id);
			var toolBaseDef = itemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float grindAmount = Constants.WeldAndGrindSpeed * speedMultiplier * MyAPIGateway.Session.GrinderSpeedMultiplier;

			Vector3D ec = Utilities.GetEngineerCenter(character);
			Vector3D bp;
			if(!Collisions.GetNearestCollisionCenter(block, ec, out bp))
				block.ComputeWorldCenter(out bp);

			SetPause(1.0);
			while (IsPaused())
			{
				CharacterRotateTo(bp);
				yield return null;
			}

			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {IJK(ijk)}";

			// Apply grinding

			var integrity0 = block.Integrity;
			
			EnableEffect(block, MyParticleEffectsNameEnum.ShipGrinder);
			EnableSound("ToolPlayGrindMetal");

			var current = new Dictionary<string, double>();
			InventoryDelta(inventory, current, +1);

			Dictionary<string, int> stock = new Dictionary<string, int>();

			for(;;)
			{	
				// Check if inventory can accept at least one unit of any stockpile component
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
				{	DisableEffectAndSound();

					var p0 = integrity0 / block.MaxIntegrity;
					var p1 = block.Integrity / block.MaxIntegrity;

					StringBuilder sb = new StringBuilder();
					sb.Append($"Block integrity changed from {Percent(p0)} to {Percent(p1)}\n");
					sb.Append($"Inventory change:\n");

					InventoryDelta(inventory, current, -1);
					InventoryDiff(current, sb);

					sb.Append($"Your inventory is full.\n");

					yield return sb.ToString();
				}

				block.DecreaseMountLevel(grindAmount, inventory);
				block.MoveItemsFromConstructionStockpile(inventory);

				// Handle block destruction
				if (block.IsDestroyed && block.StockpileEmpty)
				{
					DisableEffectAndSound();
					
					block.SpawnConstructionStockpile();
					block.CubeGrid.RazeBlock(block.Min);

					StringBuilder sb = new StringBuilder();
					sb.Append($"Inventory change:\n");

					InventoryDelta(inventory, current, -1);
					InventoryDiff(current, sb);

					sb.Append($"Done! {Commands.Name(block)} has been removed.");

					yield return sb.ToString();
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
	
		internal static string MissingComponentsText(IMySlimBlock block)
		{	Dictionary<string, int> missing = new Dictionary<string, int>();
			block.GetMissingComponents(missing);

			StringBuilder sb = new StringBuilder();
			bool first = true;
			foreach (var kv in missing)
			{	if (!first) sb.Append(", ");
				sb.Append($"{kv.Value} {Quote(kv.Key)}");
				first = false;
			}
			return $"Missing: {sb.ToString()}";
		}

		internal IEnumerator Weld(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if (block == null) yield return $"Error: no block at {IJK(ijk)}";

			if (block.Integrity >= block.MaxIntegrity)
				yield return "The block is fully intact; No repairs needed.";

			if (IsTooFar(ijk, out message)) yield return message;

			if (!EquipTool("Welder"))
				yield return "Cannot equip handheld welder. Do you have a welder in your inventory?";

			var inventory = character.GetInventory();
			if (inventory == null) yield return IE_NO_INVENTORY;

			var equippedTool = character.EquippedTool as IMyWelder;
			if (equippedTool == null) yield return "Internal error: equippedTool is not IMyWelder";

			float speedMultiplier = 1.0f;

			var item = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var itemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id);
			var toolBaseDef = itemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float weldAmount = Constants.WeldAndGrindSpeed * speedMultiplier * MyAPIGateway.Session.WelderSpeedMultiplier;

			// Check if block can accept components from inventory
			if (!block.CanContinueBuild(inventory)) yield return MissingComponentsText(block);

			Vector3D ec = Utilities.GetEngineerCenter(character);
			Vector3D bp;
			if(!Collisions.GetNearestCollisionCenter(block, ec, out bp))
				block.ComputeWorldCenter(out bp);

			SetPause(1.0);
			while (IsPaused())
			{
				CharacterRotateTo(bp);
				yield return null;
			}

			block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return  $"Error: no block at {IJK(ijk)}";

			// Apply welding
			var integrity0 = block.Integrity;

			EnableEffect(block, MyParticleEffectsNameEnum.WelderContactPoint);
			EnableSound("ToolPlayWeldMetal");

			var current = new Dictionary<string, double>();
			InventoryDelta(inventory, current, +1);

			for (;;)
			{
				block.MoveItemsToConstructionStockpile(inventory);

				var pbi = block.Integrity;

				block.IncreaseMountLevel(weldAmount, character.ControllerInfo.ControllingIdentityId, inventory, 1.0f);

				if (block.Integrity >= block.MaxIntegrity)
				{
					DisableEffectAndSound();

					StringBuilder sb = new StringBuilder();
					sb.Append($"Inventory change:\n");
					InventoryDelta(inventory, current, -1);
					InventoryDiff(current, sb);
					sb.Append("Done! Block integrity is full.");

					yield return sb.ToString();
				}
				else if (block.Integrity == pbi)
				{
					DisableEffectAndSound();

					var p0 = integrity0 / block.MaxIntegrity;
					var p1 = block.Integrity / block.MaxIntegrity;

					StringBuilder sb = new StringBuilder();
					sb.Append($"Inventory change:\n");
					InventoryDelta(inventory, current, -1);
					InventoryDiff(current, sb);
					sb.Append($"Block integrity changed from {Percent(p0)} to {Percent(p1)}\n{MissingComponentsText(block)}");

					yield return sb.ToString();
				}

				yield return null;
			}
		}
	}
}
