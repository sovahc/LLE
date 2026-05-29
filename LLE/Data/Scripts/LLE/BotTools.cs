using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRage.Game.ModAPI;
using System.Collections.Generic;
using System.Linq;
using VRage.ObjectBuilders;

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

			// Calculate damage amount per tick
			// Base rate: 2/30 per second (from AiEnabled logic), interval: 250ms
			const float BaseGrindRatePerSecond = 2.0f / 30.0f;
			const float ToolCooldownSeconds = 1.0f;

			float grindAmount = BaseGrindRatePerSecond * speedMultiplier *
								MyAPIGateway.Session.GrinderSpeedMultiplier * ToolCooldownSeconds;

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
				else
					MyConsole.Add($"- {c.Definition.Id}");
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

			//var ParticleMatrix = bot.WorldMatrix;
			//var position = ParticleMatrix.Translation;
			//MyParticleEffect particle;
			//if (MyParticlesManager.TryCreateParticleEffect(ParticleName, ref ParticleMatrix, ref position, uint.MaxValue, out particle))			{
		  	//	particle.UserScale = 3;
		  	//	//particle.OnDelete += Particle_OnDelete;
			//}

			// Play 3D sound
			//var emitter = new MyEntity3DSoundEmitter(bot as MyEntity);
			//emitter.SetPosition((Vector3)bot.GetPosition());
			//var soundPair = new MySoundPair("ToolPlayGrindMetal");
			//emitter.PlaySoundWithDistance(soundPair.SoundId);
			//emitter.Update();
			// Spawn particles (local only, for multiplayer sync use packets)
			//MyParticleEffect effect;
			//if(MyParticlesManager.TryCreateParticleEffect("MaterialHit_MoonSoil", fo.WorldMatrix, out effect))
			//{	effect.UserRadiusMultiplier = 0.4f;
			//	effect.UserLifeMultiplier = 0.4f;
			//}

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
