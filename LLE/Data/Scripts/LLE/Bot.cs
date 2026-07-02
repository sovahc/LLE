using System;
using System.Collections.Generic;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using VRageMath;

namespace LLE
{
	static class Bot
	{
		internal static IMyPlayer GetPlayerById(long EntityId)
		{
			var players = new List<IMyPlayer>();
			MyAPIGateway.Players.GetPlayers(players);

			for (int i = 0; i < players.Count; ++i)
			{
				var p = players[i];
				if (p.Character.EntityId == EntityId) return p;
			}
			return null;
		}

		internal static IMyCharacter Spawn(IMyPlayer owner)
		{
			// Spawns a dummy bot, extracts its controller, and removes the dummy.

			var dummyId = MyVisualScriptLogicProvider.SpawnBot("SpaceSpider",
				Vector3D.Zero, Vector3.Forward, Vector3.Up, "");
			if (dummyId == 0) return null;

			var dummy = MyEntities.GetEntityById(dummyId) as IMyCharacter;
			if (dummy == null) throw new Exception("Spawn: Failed to get entity");

			var dummyPlayer = GetPlayerById(dummyId);
			if (dummyPlayer == null) throw new Exception("GetPlayerById(dummyId) failed");
			var controller = dummyPlayer.Controller;
			
			// Put bot and owner in the same faction.
			var ownerFaction = MyAPIGateway.Session.Factions.TryGetPlayerFaction(owner.IdentityId);
			string factionTag;

			if (ownerFaction != null)
			{	factionTag = ownerFaction.Tag;
			}
			else
			{	factionTag = "LLE";
				MyVisualScriptLogicProvider.CreateFaction(owner.IdentityId, factionTag, factionTag, factionTag);
					// This function may fail silently.
			}

			if (!MyVisualScriptLogicProvider.SetPlayersFaction(dummyPlayer.IdentityId, factionTag))
				MyConsole.Add("Failed to set bot faction: " + factionTag);

			//

			dummy.Delete();

			//var subType = "Default_Astronaut";
			var forward = owner.Character.WorldMatrix.Forward;
			var up = owner.Character.WorldMatrix.Up;
			var spawnAt = owner.GetPosition() + forward * 2;

			var ob = new MyObjectBuilder_Character()
			{
				Name = "Gemma",
				DisplayName = null,
				SubtypeName = "CharacterTarget_Dummy",
				CharacterModel = "Target Dummy",
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
				ColorMaskHSV = MyColorPickerConstants.HSVToHSVOffset(new Vector3(0.16f, 0.85f, 1f))
			};

			var bot = MyEntities.CreateFromObjectBuilder(ob, true) as IMyCharacter;
			if (bot == null) return null;

			bot.Save = false;
			bot.Synchronized = true;
			bot.Flags &= ~VRage.ModAPI.EntityFlags.NeedsUpdate100;

			if (bot.PositionComp.GetPosition() == Vector3D.Zero) bot.SetPosition(spawnAt); // ??

			MyEntities.Add((MyEntity)bot, true);

			controller.TakeControl(bot);

			return bot;
		}
	}
}
