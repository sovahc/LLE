using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Definitions;

namespace LLE
{
	public partial class Commands
	{
		private const string RouteUsage = "Usage: route 3 2 4 3 6 9";

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

			Vector3I a, b;
			if (!tp.NextVector3I(out a)) yield return "Error: expected two positions. " + RouteUsage;
			
			if (!tp.NextVector3I(out b)) yield return "Error: expected a second position. " + RouteUsage;
			if (!tp.End) yield return "Error: too many arguments. " + RouteUsage;

			var blockA = selectedGrid.GetCubeBlock(a);
			if (blockA == null)
				yield return $"Error: no block at {IJK(a)}. A route runs between two blocks that already stand on the grid.";

			var blockB = selectedGrid.GetCubeBlock(b);
			if (blockB == null)
				yield return $"Error: no block at {IJK(b)}. A route runs between two blocks that already stand on the grid.";

			if (a == b) yield return "Error: both positions point at the same cell.";

			var portsA = new List<Base6Directions.Direction>();
			var portsB = new List<Base6Directions.Direction>();
			ConveyorPorts.AtCell(blockA, a, portsA);
			ConveyorPorts.AtCell(blockB, b, portsB);

			if (portsA.Count == 0) yield return NoPortsHere(blockA, a);
			if (portsB.Count == 0) yield return NoPortsHere(blockB, b);

			// Already face to face — the only case with nothing to build.
			Vector3I delta = b - a;
			if (Math.Abs(delta.X) + Math.Abs(delta.Y) + Math.Abs(delta.Z) == 1)
			{
				var towards = DirOf(delta);
				if (ConveyorPorts.Contains(portsA, towards) &&
					ConveyorPorts.Contains(portsB, Base6Directions.GetFlippedDirection(towards)))
				{
					yield return Success($"{Quote(Name(blockA))} at {IJK(a)} and {Quote(Name(blockB))} at {IJK(b)}"
						+ " already touch port to port. There is nothing to build.");
				}
			}

			if (ConveyorAStar.TooBig(selectedGrid, a, b))
				yield return "Error: those two blocks are too far apart to search between. Route to something nearer first.";

			var search = new ConveyorAStar(selectedGrid, a, b, portsA, portsB);
			while (!search.Tick()) yield return null;

			var path = search.Result;

			if (path.Count == 0)
			{
				if (search.Exhausted)
					yield return "Error: gave up searching for a route. Route to a nearer block first, then onward from there.";

				yield return $"Error: no run of empty cells connects {Quote(Name(blockA))} at {IJK(a)}"
					+ $" to {Quote(Name(blockB))} at {IJK(b)}. Everything between them is occupied,"
					+ " or their ports do not look toward each other. Use `orient` on the target to see where its ports are.";
			}

			var straight = ConveyorPorts.FindPiece(selectedGrid.GridSizeEnum, false);
			var curved = ConveyorPorts.FindPiece(selectedGrid.GridSizeEnum, true);

			if (straight == null || curved == null)
				yield return "Internal error: no straight and curved conveyor tube found for this grid size.";

			int pieces = path.Count - 2;

			var md = new MyMarkdown();
			md.Append($"Route from {Quote(Name(blockA))} at {IJK(a)} to {Quote(Name(blockB))} at {IJK(b)}: {pieces} pieces.");
			md.Append("Legend: `Name at I J K, ports D1 D2` means put a block called Name into that empty cell,"
				+ " turned so that its two conveyor ports look along D1 and D2."
				+ " Run `orient 'Name' I J K ports D1 D2` to get the line that builds it.");

			for (int i = 1; i < path.Count - 1; ++i)
			{
				Vector3I back = path[i - 1] - path[i];
				Vector3I forward = path[i + 1] - path[i];

				var def = back == -forward ? straight : curved;

				md.Append($"* {Quote(def.DisplayNameText)} at {IJK(path[i])}, ports {Dir(back)} {Dir(forward)}");
			}

			md.Append("Build them in this order, one at a time: `orient`, `place`, `fly I J K weld`, `weld`."
				+ " After a piece is welded, run `route` again from that piece to the same target"
				+ " and it answers with what is left — you do not have to remember the rest.");

			yield return Success(md.Result());
		}
	}
}
