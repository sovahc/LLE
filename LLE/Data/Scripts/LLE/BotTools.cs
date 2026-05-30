using Sandbox.Definitions;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using Sandbox.Game.Entities;
using VRage.Game.Entity;
using VRage.Game;
using VRage.Game.ModAPI;
using System.Collections.Generic;
using VRageMath;

namespace LLE
{
	public class BotTools
	{
		private readonly float WELD_AND_GRIND_SPEED = 0.03f;
		private readonly float SOUND_VOLUME = 0.5f;
		
		private IMySlimBlock targetBlock;
		private IMyCharacter bot;
		
		private MyEntity3DSoundEmitter soundEmitter;
		private MyParticleEffect particleEffect;

		public BotTools(IMyCharacter bot_)
		{	bot = bot_;
		}

		internal void Stop()
		{	targetBlock = null;
			
			if(soundEmitter != null)
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

		internal bool GrindBlock()
		{
			var block = targetBlock;
			if(block == null) return false;

			var inventory = bot.GetInventory();
			if (inventory == null) return false;

			var equippedTool = bot.EquippedTool as IMyAngleGrinder;
			if (equippedTool == null) return false;

			float speedMultiplier = 1.0f;

			var item = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var itemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id);
			var toolBaseDef = itemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float grindAmount = WELD_AND_GRIND_SPEED * speedMultiplier * MyAPIGateway.Session.GrinderSpeedMultiplier;

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

  			var sound = "ToolPlayGrindMetal";

			if(soundEmitter == null)
			{	soundEmitter = new MyEntity3DSoundEmitter(bot as MyEntity);
				soundEmitter.VolumeMultiplier = SOUND_VOLUME;
				soundEmitter.PlaySound(new MySoundPair(sound));
			}

			if (particleEffect == null)
			{
				var particleName = MyParticleEffectsNameEnum.ShipGrinder;
				MatrixD m = MatrixD.Identity;
				Vector3D pos = Vector3D.Zero;
				if (MyParticlesManager.TryCreateParticleEffect(particleName, ref m, ref pos, uint.MaxValue, out particleEffect))
					particleEffect.UserRadiusMultiplier = 2f;
			}
			if (particleEffect != null)
			{
				BoundingBoxD box;
				block.GetWorldBoundingBox(out box, false);
				particleEffect.WorldMatrix = box.Matrix;
			}

			// Handle block destruction
			if (block.IsDestroyed && block.StockpileEmpty)
			{
				block.SpawnConstructionStockpile();
				block.CubeGrid.RazeBlock(block.Min);
			}

			return true;
		}

		internal bool WeldBlock()
		{
			var block = targetBlock;
			if(block == null) return false;

			var inventory = bot.GetInventory();
			if (inventory == null) return false;

			var equippedTool = bot.EquippedTool as IMyWelder;
			if (equippedTool == null) return false;

			float speedMultiplier = 1.0f;

			var item = MyDefinitionManager.Static.GetPhysicalItemForHandItem(equippedTool.DefinitionId);
			var itemDef = MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id);
			var toolBaseDef = itemDef as MyEngineerToolBaseDefinition;

			if (toolBaseDef != null)
				speedMultiplier = toolBaseDef.SpeedMultiplier;

			float weldAmount = WELD_AND_GRIND_SPEED * speedMultiplier * MyAPIGateway.Session.WelderSpeedMultiplier;

			// Check if block can accept components from inventory
			if (!block.CanContinueBuild(inventory)) return false;

			// Apply welding
			block.MoveItemsToConstructionStockpile(inventory);
			block.IncreaseMountLevel(weldAmount, bot.ControllerInfo.ControllingIdentityId, inventory, 1.0f);

			var sound = "ToolPlayWeldMetal";

			if(soundEmitter == null)
			{
				soundEmitter = new MyEntity3DSoundEmitter(bot as MyEntity);
				soundEmitter.VolumeMultiplier = SOUND_VOLUME;
				soundEmitter.PlaySound(new MySoundPair(sound));
			}

			if (particleEffect == null)
			{
				var particleName = MyParticleEffectsNameEnum.WelderContactPoint;
				MatrixD m = MatrixD.Identity;
				Vector3D pos = Vector3D.Zero;
				if (MyParticlesManager.TryCreateParticleEffect(particleName, ref m, ref pos, uint.MaxValue, out particleEffect))
					particleEffect.UserRadiusMultiplier = 4f;
			}
			if (particleEffect != null)
			{
				BoundingBoxD box;
				block.GetWorldBoundingBox(out box, false);
				particleEffect.WorldMatrix = box.Matrix;
			}

			return true;
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

		internal void SetTargetBlock(IMySlimBlock block)
		{
			targetBlock = block;
		}
	}
}
