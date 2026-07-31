using System.Collections;

using VRageMath;
using VRage.Game.Entity;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.Game.Components;

namespace LLE
{
	public partial class Commands
	{	
		internal static bool IsSurvivalKit(IMyTerminalBlock tb)
		{
			return tb.SlimBlock.BlockDefinition is MySurvivalKitDefinition;
		}

		internal CommandResult GetRechargePoints(TokenParser tp)
		{
			string message;
			if(!GridIsSet(out message)) return message;

			var grid = selectedGrid;

			string category, name;
			Description(grid as MyEntity, out category, out name);

			bool hasPower = GridHasPower(grid);

			if(!hasPower) return Success("Grid has no power. Cannot recharge from unpowered grid.");

			var md = new MyMarkdown();
			md.Append($"# Recharge points of {Quote(name)}");

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
			terminalBlocks.Clear();
			ts.GetBlocks(terminalBlocks);

			bool any = false;

			foreach (var block in terminalBlocks)
			{
				if (block.CubeGrid != grid) continue;

				if (IsSurvivalKit(block) || block is IMyCockpit || block is IMyMedicalRoom)
				{
					bool hasHydrogen = IsHydrogenReachable(block, terminalBlocks);

					string occupiedBy = "";
					var cockpit = block as IMyCockpit;
					if (cockpit != null && cockpit.Pilot != null)
						occupiedBy = $" (occupied by {cockpit.Pilot.DisplayName})";

					string ecat;

					// hasPower is true

					if(hasHydrogen)
						ecat = "## Energy and Hydrogen";
					else
						ecat = "## Energy";

					if(ecat != null)
					{	md.Add(ecat, $"* {Name(block.SlimBlock)} at {IJK(block.Position)}{occupiedBy}");
						any = true;
					}
				}
			}
			terminalBlocks.Clear();

			if(!any)
				md.Append(" -- none --");

			return Success(md.Result());
		}

		internal IEnumerator Recharge(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;
			if (CurrentGridIsProjection(out message)) yield return message;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if (block == null) yield return $"Error: no block at {IJK(ijk)}";

			if(block.FatBlock == null) yield return $"There is no way to recharge from {Name(block)}";

			var cockpit = block.FatBlock as IMyCockpit;
			if(cockpit != null)
			{	if(!cockpit.IsFunctional) yield return $"Error: {Name(block)} at {IJK(ijk)} is not functional.";

				if(!IsAtInteractionPoint(block, InteractionKind.Inventory, out message))
					yield return message;

				if(!EnterCockpit(cockpit, out message)) yield return message;

				const double wait = 2.5;

				var t1 = Time.Now + wait;

				bool anyProgress = false;

				var maximal = status.Maximal();
				var initial = status.Current();
				var previous = initial;

				for(int i = 0; i < 20; ++i)
				{
					while(Time.Now < t1) yield return null;
					t1 += wait;

					var current = status.Current();

					bool progress = Status.IsCharging(previous, current);

					previous = current;

					anyProgress |= progress;

					if(!progress)
					{	if(!anyProgress)
						{	cockpit.RemovePilot();
							yield return "Block is not charging!";
						}

						cockpit.RemovePilot();
						yield return Success(status.ReportAll());
					}
				}

				cockpit.RemovePilot();
				yield return "Timeout! Recharge may be too slow.";
			}

			var tb = block.FatBlock as IMyTerminalBlock;
			if(tb == null || !(IsSurvivalKit(tb) || tb is IMyMedicalRoom))
				yield return $"There is no way to recharge from {Name(block)} at {IJK(ijk)}.";

			if(IsSurvivalKit(tb) || tb is IMyMedicalRoom)
			{
				if(!IsAtInteractionPoint(block, InteractionKind.Recharge, out message))
					yield return message;

				var ec = GetEngineerCenter();
				Vector3D rechargeButton;
				if(!Collisions.GetNearestDetectorCenterByPrefix(block, ec, "block_", out rechargeButton))
					yield return $"Recharge button not found on {Name(block)}";

				bool hasPower = GridHasPower(selectedGrid);

				var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(selectedGrid);
				terminalBlocks.Clear();
				ts.GetBlocks(terminalBlocks);
				
				bool hasHydrogen = IsHydrogenReachable(block.FatBlock, terminalBlocks);
				terminalBlocks.Clear();

				if(!hasPower && !hasHydrogen)
					yield return "No power or hydrogen available for recharging.";

				// Simulating the process since the game once again locked the necessary API

				SetPause(Constants.MicronavigationDelay);
				while(IsPaused())
				{	CharacterRotateTo(rechargeButton);
					yield return null;
				}

				var oc = character.Components?.Get<MyCharacterOxygenComponent>();
				if(oc == null) yield return "Internal error: Character has no MyCharacterOxygenComponent.";

				var sc = character.Components?.Get<MyCharacterStatComponent>();
				if(sc == null) yield return "Internal error: Character has no MyCharacterStatComponent.";

				const float HealthFillSeconds = 8.0f;
				const float EnergyFillSeconds = 5.6f;
				const float HydrogenFillSeconds = 1.7f;

				PlaySound("BlockMedicalProgress");

				for(;;)
				{	double t0 = Time.Now;
					yield return null;
					
					float dt = (float)(Time.Now - t0);

					bool needMoreHealth = true;
					bool needMoreEnergy = true;
					bool needMoreHydrogen = true;

					{
						float max = sc.Health.MaxValue;
						float current = sc.Health.Value;
						float rate = dt / HealthFillSeconds;
						float target = current + max * rate;
						if(target > max) { target = max; needMoreHealth = false; }
						sc.Health.Value = target;
					}

					if(hasPower)
					{
						float max = Constants.CharacterBatteryMWh;
						float current = oc.CharacterGasSource.RemainingCapacityByType(electricityId);
						float rate = dt / EnergyFillSeconds;
						float target = current + max * rate;
						if(target > max) { target = max; needMoreEnergy = false; }
						oc.CharacterGasSource.SetRemainingCapacityByType(electricityId, target);
					}

					if(hasHydrogen)
					{
						float max = 1.0f;
						float current = oc.GetGasFillLevel(hydrogenId);
						float rate = dt / HydrogenFillSeconds;
						float target = current + max * rate;
						if(target > 1f) { target = max; needMoreHydrogen = false; }
						var hId = hydrogenId;
						oc.UpdateStoredGasLevel(ref hId, target);
					}

					if(needMoreHealth) continue;
					if(hasPower && needMoreEnergy) continue;
					if(hasHydrogen && needMoreHydrogen) continue;

					StopSound();
					yield return Success(status.ReportAll());
				}
			}
		}
	}
}
