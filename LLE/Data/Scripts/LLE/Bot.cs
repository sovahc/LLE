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
		static IMyEntityController _controller;

		/// <summary>
		/// Call once on mod init. Spawns a dummy bot, extracts its controller, and removes the dummy.
		/// </summary>
		internal static bool Init()
		{
			if (_controller != null) return true;

			var dummyId = MyVisualScriptLogicProvider.SpawnBot("SpaceSpider", Vector3D.Zero, Vector3.Forward, Vector3.Up, "");
			if (dummyId == 0)
				return false;

			// Give the game a moment to create the bot entity
			var dummy = MyEntities.GetEntityById(dummyId) as IMyCharacter;
			if (dummy == null)
				return false;

			// Find the bot player that owns this character
			var players = new List<IMyPlayer>();
			MyAPIGateway.Players.GetPlayers(players);

			for (int i = 0; i < players.Count; ++i)
			{
				var p = players[i];
				if (p?.IsBot == true && p.Character?.EntityId == dummyId && p.Controller != null)
				{
					_controller = p.Controller;

					// Remove from default hostile faction so the controller stays neutral
					var faction = MyAPIGateway.Session.Factions?.TryGetPlayerFaction(p.IdentityId);
					if (faction != null)
					{
						try { MyAPIGateway.Session.Factions.KickMember(faction.FactionId, p.IdentityId); }
						catch { /* Factions disabled or unavailable */ }
					}

					break;
				}
			}

			// Clean up the dummy bot
			dummy?.Delete();

			return _controller != null;
		}

		internal static IMyCharacter Spawn(IMyPlayer owner)
		{
			if(_controller == null) return null;

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
				ColorMaskHSV = new Vector3(0, 0, 0.05f),
			};

			var bot = MyEntities.CreateFromObjectBuilder(ob, true) as IMyCharacter;
			if (bot == null) return null;
	
			bot.Save = false;
			bot.Synchronized = true;
			bot.Flags &= ~VRage.ModAPI.EntityFlags.NeedsUpdate100;

			if (bot.PositionComp.GetPosition() == Vector3D.Zero) bot.SetPosition(spawnAt); // ??

			MyEntities.Add((MyEntity)bot, true);

			_controller.TakeControl(bot);

			return bot;
		}
	}
}