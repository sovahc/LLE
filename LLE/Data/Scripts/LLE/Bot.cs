using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace LLE
{
	static class Bot
	{
		internal static IMyCharacter Spawn(IMyPlayer owner)
		{
			var subType = "Default_Astronaut";
			//var subType = "CharacterTarget_Dummy";
			//var subType = "Target_Dummy";
			var forward = owner.Character.WorldMatrix.Forward;
			var up = owner.Character.WorldMatrix.Up;
			var spawnAt = owner.GetPosition() + forward * 2;

			var ob = new MyObjectBuilder_Character()
			{
				Name = "Gemma",
				DisplayName = null,
				SubtypeName = subType,
				CharacterModel = subType,
				EntityId = 0,
				AIMode = false,
				JetpackEnabled = MyAPIGateway.Session.SessionSettings.EnableJetpack,
				EnableBroadcasting = true,
				NeedsOxygenFromSuit = false,
				OxygenLevel = 1,
				MovementState = MyCharacterMovementEnum.Flying,
				PersistentFlags = MyPersistentEntityFlags2.InScene | MyPersistentEntityFlags2.Enabled,
				PositionAndOrientation = new MyPositionAndOrientation(spawnAt, forward, up),
				Health = 1000,
				OwningPlayerIdentityId = owner.IdentityId,
				ColorMaskHSV = new Vector3(0, 0, 0.05f),
			};

			var bot = MyEntities.CreateFromObjectBuilder(ob, true) as IMyCharacter;
			if (bot == null) return null;
	
			bot.Save = false;
			bot.Synchronized = true;
			bot.Flags &= ~VRage.ModAPI.EntityFlags.NeedsUpdate100;

			if (bot.PositionComp.GetPosition() == Vector3D.Zero) bot.SetPosition(spawnAt); // ??

			MyEntities.Add((MyEntity)bot, true);

			//owner.Controller.TakeControl(bot);
			return bot;

			//bot.Physics.LinearVelocity = grid.Physics.LinearVelocity;
			//var controlEnt = bot as Sandbox.Game.Entities.IMyControllableEntity;
			//if (controlEnt != null && controlEnt.RelativeDampeningEntity?.EntityId != grid.EntityId)
			//controlEnt.RelativeDampeningEntity = grid;
			//var players = new List<IMyPlayer>();
			//MyAPIGateway.Players.GetPlayers(players);
			//for (int i = 0; i < players.Count; ++i)
			//{	var player = _tempPlayers[i];
			//	var entityId = player?.Character?.EntityId ?? -1;
			//	if (player?.IsBot == true && player.Controller != null)
		}
	}
}