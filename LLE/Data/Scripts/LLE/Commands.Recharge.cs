using System.Collections;

using VRageMath;
using VRage.Game.Entity;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using Sandbox.Game.Entities.Character.Components;
using VRage.Game;
using VRage.Game.ObjectBuilders.Definitions;

namespace LLE
{
	public partial class Commands
	{	
		private static readonly MyDefinitionId electricityId =
    		new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

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

					if(hasPower && hasHydrogen)
						ecat = "## Energy and Hydrogen";
					else if (hasPower)
						ecat = "## Energy";
					else if (hasHydrogen)
						ecat = "## Hydrogen";
					else ecat = null;

					if(ecat != null)
						md.Add(ecat, $"* {Name(block.SlimBlock)} at {IJK(block.Position)}{occupiedBy}");
				}
			}
			terminalBlocks.Clear();

			return Success(md.Result());
		}

		internal IEnumerator Recharge(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if (block == null) yield return $"Error: no block at {IJK(ijk)}";

			if(block.FatBlock == null) yield return $"There is no way to recharge from {Name(block)}";

			var cockpit = block.FatBlock as IMyCockpit;
			if(cockpit != null)
			{	if(!cockpit.IsFunctional) yield return $"Error: {Name(block)} at {IJK(ijk)} is not functional.";

				if(IsTooFar(ijk, out message)) yield return message;

				if(!EnterCockpit(cockpit, out message)) yield return message;

				var t1 = Time.Now + 1;

				bool anyProgress = false;

				var maximal = status.Maximal();
				var initial = status.Current();
				var previous = initial;

				for(int i = 0; i < 20; ++i)
				{
					while(Time.Now < t1) yield return null;
					t1 += 2.5;

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
						yield return Success(status.ReportAll()); // not fully correct
					}
				}

				cockpit.RemovePilot();
				yield return $"Timeout! Recharge may be too slow.";
			}

			var tb = block.FatBlock as IMyTerminalBlock;
			if(IsSurvivalKit(tb) || tb is IMyMedicalRoom)
			{
				if(IsTooFar(ijk, out message)) yield return message;

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

				// симуляция процесса, так как игра в очередной раз зажала нужный API

				SetPause(Constants.DelayForRotation);
				while(IsPaused())
				{	CharacterRotateTo(rechargeButton);
					yield return null;
				}

				var oc = character.Components?.Get<MyCharacterOxygenComponent>();
    			if(oc == null) yield return "Internal error: Нет MyCharacterOxygenComponent у персонажа.";

				const float ChargeFillSeconds = 1.666f;

				for(;;)
				{	double t0 = Time.Now;
					yield return null;
					
					float dt = (float)(Time.Now - t0);
					float rate = dt / ChargeFillSeconds;

					bool needMorePower = true;
					bool needMoreHydrogen = true;

    				if(hasPower)
    				{	
						float max = Constants.CharacterBatteryWh;
						
						float current = oc.CharacterGasSource.RemainingCapacityByType(electricityId);
						float target = current + (max - current) * rate;
        				if(target > max) { target = max; needMorePower = false; }
        				oc.CharacterGasSource.SetRemainingCapacityByType(electricityId, target);
    				}

    				if(hasHydrogen)
    				{
        				float current = oc.GetGasFillLevel(hydrogenId);
        				float target = current + (1f - current) * rate;
        				if(target > 1f) { target = 1f; needMoreHydrogen = false; }
        				var hId = hydrogenId;
        				oc.UpdateStoredGasLevel(ref hId, target);
    				}

					if(hasPower && needMorePower) continue;
					if(hasHydrogen && needMoreHydrogen) continue;

					yield return Success(status.ReportAll());
				}
			}
		}
	}
}
