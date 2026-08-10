using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;

namespace LLE
{
	public partial class Commands
	{
		private string NoPortsHere(IMySlimBlock block, Vector3I cell)
		{
			var all = new List<ConveyorPort>();
			ConveyorPorts.OfBlock(block, all);

			if (all.Count == 0)
				return $"Error: {Quote(Name(block))} at {IJK(cell)} has no conveyor ports at all, so nothing can be piped to it.";

			var cells = new List<Vector3I>();
			foreach (var p in all) if (!cells.Contains(p.Cell)) cells.Add(p.Cell);

			var sb = new StringBuilder();
			sb.Append($"Error: {Quote(Name(block))} has no conveyor port on the cell {IJK(cell)}. Its ports are at ");
			for (int i = 0; i < cells.Count; ++i)
			{	if (i != 0) sb.Append("; ");
				sb.Append(IJK(cells[i]));
			}
			sb.Append(". Use one of those positions instead.");
			return sb.ToString();
		}

		private string RouteEnd(Vector3I ijk, List<ConveyorPort> ports)
		{
			var block = selectedGrid.GetCubeBlock(ijk);
			if (block != null)
			{	ConveyorPorts.OfBlock(block, ports);
				return Quote(Name(block));
			}

			DraftBlock drafted;
			if (TryGetDraftBlock(ijk, out drafted))
			{	ConveyorPorts.At(drafted.Definition,
					new MyBlockOrientation(drafted.Forward, drafted.Up), drafted.Cell, ports);
				return $"drafted {Quote(drafted.Definition.DisplayNameText)}";
			}

			ports.Clear();
			return null;
		}

		internal IEnumerator Route(ToolCall call)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;
			if (CurrentGridIsProjection(out message)) yield return message;

			Vector3I a, b;
			if (!call.Ijk(out a)) yield return call.NeedIjk;
			if (!call.Ijk2(out b)) yield return call.NeedIjk2;

			var portsA = new List<ConveyorPort>();
			var portsB = new List<ConveyorPort>();

			var nameA = RouteEnd(a, portsA);
			if (nameA == null)
				yield return $"Error: nothing at {IJK(a)}. A route runs between two blocks that stand on the grid or are in the draft.";

			var nameB = RouteEnd(b, portsB);
			if (nameB == null)
				yield return $"Error: nothing at {IJK(b)}. A route runs between two blocks that stand on the grid or are in the draft.";

			if (a == b) yield return "Error: both positions point at the same cell.";

			if (portsA.Count == 0)
				yield return $"Error: {nameA} at {IJK(a)} has no conveyor ports at all, so nothing can be piped to it.";
			if (portsB.Count == 0)
				yield return $"Error: {nameB} at {IJK(b)} has no conveyor ports at all, so nothing can be piped to it.";

			yield return Validated;

			var blocked = new List<Vector3I>();
			DraftCells(blocked);

			var search = new ConveyorAStar(selectedGrid, portsA, portsB, blocked);
			while (!search.Tick()) yield return null;

			var path = search.Result;

			if (path.Count == 0)
			{
				yield return $"Error: no run of empty cells connects"
					+ $" {nameA} at {IJK(a)} to"
					+ $" {nameB} at {IJK(b)}."
					+ (blocked.Count == 0 ? "" : " Cells held by the draft count as occupied.");
			}

			int pieces = path.Count - 2;

			if (pieces == 0)
			{
				yield return Success($"{nameA} at {IJK(path[0])} and {nameB} at {IJK(path[path.Count - 1])}"
					+ " already touch port to port. There is nothing to build.");
			}

			var md = new MyMarkdown();
			md.Append($"Route from {nameA} at {IJK(path[0])} to {nameB} at {IJK(path[path.Count - 1])} ({pieces} conveyor pieces required)");

			for (int i = 1; i < path.Count - 1; ++i)
			{
				Vector3I back = path[i - 1] - path[i];
				Vector3I forward = path[i + 1] - path[i];

				var curved = back != -forward;
				var type = curved ? "curved" : "straight";

				md.Append($"* [{type}] at {IJK(path[i])}, ports {Dir(back)} {Dir(forward)}");
			}

			yield return Success(md.Result());
		}
	}
}
