using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRage.Game.ModAPI;
using System.Collections.Generic;

namespace LLE
{
	public class BotTools
	{
		private MyEntity3DSoundEmitter emitter;

		public void Stop()
		{	if(emitter == null) return;
			emitter.StopSound(false);
			emitter = null;
		}

		public bool GrindBlock(IMyCharacter bot, IMySlimBlock block)
		{
			if(block == null) return false;

			var inventory = bot.GetInventory();
			if (inventory == null) return false;

			var equippedTool = bot.EquippedTool as IMyAngleGrinder;
			if (equippedTool == null) return false;

			float speedMultiplier = 1.0f;

			var physicalItem = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var handItemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(physicalItem.Id);
			var toolBaseDef = handItemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float grindAmount = 0.03f * speedMultiplier * MyAPIGateway.Session.GrinderSpeedMultiplier;

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
			if (!canAccept) return false;

			// Apply grinding
			block.DecreaseMountLevel(grindAmount, inventory);
			block.MoveItemsFromConstructionStockpile(inventory);

			var isWelder = false;
	  		
			var sound = isWelder ? "ToolPlayWeldMetal" : "ToolPlayGrindMetal";

			if(emitter == null)
			{	emitter = new MyEntity3DSoundEmitter(bot as MyEntity);
				emitter.VolumeMultiplier = 0.5f;
				emitter.PlaySound(new MySoundPair(sound));
			}

			var ParticleName = isWelder ? MyParticleEffectsNameEnum.WelderContactPoint : "AiEnabled_AngleGrinder";

			// TODO Particle effect

			// Handle block destruction
			if (block.IsDestroyed && block.StockpileEmpty)
			{
				block.SpawnConstructionStockpile();
				block.CubeGrid.RazeBlock(block.Min);
			}

			return true;
		}

		public static void GetStockpileComponents(IMySlimBlock block, Dictionary<string, int> components)
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
	}
}
