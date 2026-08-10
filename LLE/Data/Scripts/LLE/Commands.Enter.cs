using System.Collections;

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
			
			var currentSeat = character.Parent as IMyCockpit;
			if(currentSeat != null && currentSeat.EntityId != cockpit.EntityId)
				currentSeat.RemovePilot();

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

		internal IEnumerator Enter(ToolCall call)
		{
			string message;
			if(!GridIsSet(out message)) yield return message;
			if(CurrentGridIsProjection(out message)) yield return message;

			Vector3I ijk;
			if(!call.Ijk(out ijk)) yield return call.NeedIjk;

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) yield return $"Error: no block at {IJK(ijk)}";

			var cockpit = block.FatBlock as IMyCockpit;
			if(cockpit == null) yield return $"Error: {Name(block)} at {IJK(ijk)} is not a cockpit or seat.";

			if(!cockpit.IsFunctional) yield return $"Error: {Name(block)} at {IJK(ijk)} is not functional.";

			if(!IsAtInteractionPoint(block, InteractionKind.Inventory, out message))
				yield return message;

			yield return Validated;

			if(!EnterCockpit(cockpit, out message)) yield return message;
			yield return Success(message);
		}

		internal IEnumerator Exit()
		{
			var seat = character.Parent as IMyCockpit;
			if(seat == null) yield return "You are not seated.";

			yield return Validated;

			var blockName = seat.CustomName ?? "cockpit";
			seat.RemovePilot();

			// RemovePilot relocates the character to a free neighboring position.
			if(character.Parent != null)
				yield return $"Error: failed to leave {Quote(blockName)}.";

			yield return Success($"Left {Quote(blockName)}.");
		}
	}
}
