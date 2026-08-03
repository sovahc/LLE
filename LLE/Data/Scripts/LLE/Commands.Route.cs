using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
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

		internal IEnumerator Route(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;
			if (CurrentGridIsProjection(out message)) yield return message;

			const string usage = "Usage: route from I J K to I₂ J₂ K₂";

			Vector3I a, b;
			if (!tp.Match("from")) yield return "Error: expected 'from' " + usage;
			if (!tp.NextVector3I(out a)) yield return "Error: expected two positions. " + usage;
			if (!tp.Match("to")) yield return "Error: expected 'to' " + usage;
			if (!tp.NextVector3I(out b)) yield return "Error: expected a second position. " + usage;
			if (!tp.End) yield return "Error: too many arguments. " + usage;

			var blockA = selectedGrid.GetCubeBlock(a);
			if (blockA == null)
				yield return $"Error: no block at {IJK(a)}. A route runs between two blocks that already stand on the grid.";

			var blockB = selectedGrid.GetCubeBlock(b);
			if (blockB == null)
				yield return $"Error: no block at {IJK(b)}. A route runs between two blocks that already stand on the grid.";

			if (a == b) yield return "Error: both positions point at the same cell.";

			var portsA = new List<ConveyorPort>();
			var portsB = new List<ConveyorPort>();
			ConveyorPorts.OfBlock(blockA, portsA);
			ConveyorPorts.OfBlock(blockB, portsB);

			if (portsA.Count == 0)
				yield return $"Error: {Quote(Name(blockA))} at {IJK(a)} has no conveyor ports at all, so nothing can be piped to it.";
			if (portsB.Count == 0)
				yield return $"Error: {Quote(Name(blockB))} at {IJK(b)} has no conveyor ports at all, so nothing can be piped to it.";

			var search = new ConveyorAStar(selectedGrid, portsA, portsB);
			while (!search.Tick()) yield return null;

			var path = search.Result;

			if (path.Count == 0)
			{
				yield return $"Error: no run of empty cells connects"
					+ $" {Quote(Name(blockA))} at {IJK(a)} to"
					+ $" {Quote(Name(blockB))} at {IJK(b)}.";
			}

			int pieces = path.Count - 2;

			if (pieces == 0)
			{
				yield return Success($"{Quote(Name(blockA))} at {IJK(path[0])} and {Quote(Name(blockB))} at {IJK(path[path.Count - 1])}"
					+ " already touch port to port. There is nothing to build.");
			}

			var md = new MyMarkdown();
			md.Append($"Route from {Quote(Name(blockA))} at {IJK(path[0])} to {Quote(Name(blockB))} at {IJK(path[path.Count - 1])} ({pieces} conveyor pieces required)");

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
