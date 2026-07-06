using VRageMath;
using Sandbox.ModAPI;

namespace LLE
{
	public partial class Commands
	{
		private bool EnterCockpit(IMyCockpit cockpit, out string message)
		{	
			var block = cockpit.SlimBlock;
			var ijk = cockpit.Position;
			
			// Already seated somewhere — get out first.
			var currentSeat = character.Parent as IMyCockpit;
			if(currentSeat != null && currentSeat.EntityId != cockpit.EntityId)
				currentSeat.RemovePilot();

			// Already in this seat — nothing to do.
			if(cockpit.Pilot != null && cockpit.Pilot.EntityId == character.EntityId)
			{	message = $"Already in {Name(block)} at {IJK(ijk)}.";
				return true;
			}

			if(cockpit.Pilot != null)
			{	message = $"Error: {Name(block)} at {IJK(ijk)} is already occupied.";
				return false;
			}

			cockpit.AttachPilot(character, 0);

			// AttachPilot silently no-ops if it fails; verify by re-checking Pilot.
			if(cockpit.Pilot != null && cockpit.Pilot.EntityId == character.EntityId)
			{	message = $"Entered {Name(block)} at {IJK(ijk)}.";
				return true;
			}
			else
			{	message = $"Error: failed to enter {Name(block)} at {IJK(ijk)}.";
				return false;
			}
		}

		internal CommandResult Enter(TokenParser tp)
		{
			string message;
			if(!GridIsSet(out message)) return message;

			Vector3I ijk;
			if(!tp.NextVector3I(out ijk)) return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) return $"Error: no block at {IJK(ijk)}";

			var cockpit = block.FatBlock as IMyCockpit;
			if(cockpit == null) return $"Error: {Name(block)} at {IJK(ijk)} is not a cockpit or seat.";

			if(!cockpit.IsFunctional) return $"Error: {Name(block)} at {IJK(ijk)} is not functional.";

			if(!IsAtInventoryPoint(block, out message)) return message;

			if(!EnterCockpit(cockpit, out message)) return message;
			return Success(message);
		}

		internal CommandResult Exit(TokenParser tp)
		{
			var seat = character.Parent as IMyCockpit;
			if(seat == null) return "You are not seated.";

			var blockName = seat.CustomName ?? "cockpit";
			seat.RemovePilot();

			// RemovePilot itself relocates the character to a free neighbouring position.
			if(character.Parent != null)
				return $"Error: failed to leave {Quote(blockName)}.";

			return Success($"Left {Quote(blockName)}.");
		}
	}
}
