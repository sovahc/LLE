using System.Collections;

using VRageMath;
using VRage.Game.Entity;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;

namespace LLE
{
	public partial class Commands
	{	
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

				var def = block.SlimBlock.BlockDefinition;
				if (def is MySurvivalKitDefinition ||
					block is IMyCockpit ||
					block is IMyMedicalRoom)
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

			if(block.FatBlock == null) yield return $"There is no way to recharge from {IJK(ijk)}";

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
		}
	}
}
