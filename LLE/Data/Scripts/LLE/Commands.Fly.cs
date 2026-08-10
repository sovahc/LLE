using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using System;

using DoorStatus = Sandbox.ModAPI.Ingame.DoorStatus;

namespace LLE
{
	public partial class Commands
	{
		private readonly MicroNavigation micro = new MicroNavigation();
		private readonly DampedSpringController springController = new DampedSpringController();

		private AStarHelper aStarHelper;

		internal bool IsEngineerInsideGrid(Vector3D engineer, IMyCubeGrid grid)
		{
			int border = 0;
			if(grid.GridSizeEnum == MyCubeSize.Large) border = 1;
			if(grid.GridSizeEnum == MyCubeSize.Small) border = 6;

			var local = grid.WorldToGridInteger(engineer);
			return local.X >= grid.Min.X - border && local.X <= grid.Max.X + border &&
				   local.Y >= grid.Min.Y - border && local.Y <= grid.Max.Y + border &&
				   local.Z >= grid.Min.Z - border && local.Z <= grid.Max.Z + border;
		}

		internal IMyCubeGrid GetCurrentEngineerGrid(Vector3D engineer)
		{
			var sphere = new BoundingSphereD(engineer, 10);
			var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref sphere);

			IMyCubeGrid result = null;
			double minimalDistanceSq = double.MaxValue;

			foreach (var e in entities)
			{
				var g = e as IMyCubeGrid;
				if (g == null || g.GridSizeEnum != MyCubeSize.Large) continue;

				if(!IsEngineerInsideGrid(engineer, g)) continue;

				double distanceSq = (engineer - g.PositionComp.WorldAABB.Center).LengthSquared();
				if(distanceSq > minimalDistanceSq) continue;

				minimalDistanceSq = distanceSq;
				result = g;
			}

			return result;
		}

		internal IMyDoor GetDoorAt(Vector3I ijk)
		{
			var block = selectedGrid?.GetCubeBlock(ijk);
			return block?.FatBlock as IMyDoor;
		}

		internal class PathNode
		{	public Vector3D v;
			public Vector3I? openDoor;
			public Vector3I? closeDoor;
		}

		private List<PathNode> MakePath(List<Vector3I> path, AStarHelper helper)
		{
			var result = new List<PathNode>();
			if (path.Count == 0) return result;

			result.Add(new PathNode() { v = helper.CellToWorld(path[0]) });
			if (path.Count == 1) return result;

			for (int i = 1; i < path.Count - 1; i++)
			{
				Vector3I prevDir = path[i] - path[i - 1];
				Vector3I nextDir = path[i + 1] - path[i];

				var doorAhead = GetDoorAt(helper.CellToBlock(path[i + 1]));
				var doorBehind = GetDoorAt(helper.CellToBlock(path[i - 1]));
				
				// Optimization: fly through open doors without stopping
				if(doorAhead != null && doorAhead.Status == DoorStatus.Open) doorAhead = null;
				if(doorBehind != null && doorBehind.Status == DoorStatus.Open) doorBehind = null;

				if (prevDir != nextDir || doorAhead != null || doorBehind != null)
					result.Add(new PathNode()
					{	v = helper.CellToWorld(path[i]),
						openDoor = doorAhead != null ? (Vector3I?)helper.CellToBlock(path[i + 1]) : null,
						closeDoor = doorBehind != null ? (Vector3I?)helper.CellToBlock(path[i - 1]) : null
					});
			}

			result.Add(new PathNode() { v = helper.CellToWorld(path[path.Count - 1]) });

			return result;
		}

		private InteractionKind? ParseIntention(string word, IMySlimBlock block)
		{
			if(string.Equals(word, "grind", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(word, "weld", StringComparison.OrdinalIgnoreCase))
				return InteractionKind.GrindWeld;

			if(string.Equals(word, "get", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(word, "put", StringComparison.OrdinalIgnoreCase) ||
			   string.Equals(word, "enter", StringComparison.OrdinalIgnoreCase))
				return InteractionKind.Inventory;

			if(string.Equals(word, "recharge", StringComparison.OrdinalIgnoreCase))
			{
				if(block?.FatBlock is IMyCockpit)
					return InteractionKind.Inventory;
				return InteractionKind.Recharge;
			}

			return null;
		}

		internal IEnumerator Approach(ToolCall call)
		{
			string message;
			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!call.Ijk(out ijk)) yield return call.NeedIjk;

			var intentionWord = call.Str("action");
			if(string.IsNullOrEmpty(intentionWord))
				yield return call.Need("action");

			var block = selectedGrid.GetCubeBlock(ijk);
			
			Vector3I destinationCell;
			string arrivalMessage;

			if(string.Equals(intentionWord, "place", StringComparison.OrdinalIgnoreCase))
			{
				if(block != null)
					yield return $"Error: {IJK(ijk)} is occupied by block {Quote(Name(block))}. Cannot build a block on an occupied cell.";

				var producer = EQS.ProduceCells(selectedGrid.Grid, ijk, GetEngineerCenter());
				var placeCells = new List<Vector3I>();
				
				foreach (var c in producer)
				{	if(c == ijk) continue;
					placeCells.Add(c);
				}

				if(placeCells.Count == 0)
					yield return $"Error: no free cells next to {IJK(ijk)}";

				destinationCell = NearestToEngineer(placeCells);
				arrivalMessage = $"Arrived next to free space at {IJK(ijk)}. Your position: {IJK(destinationCell)}";
			}
			else
			{	// calculate free cell
				var intention = ParseIntention(intentionWord, block);
				if(intention == null)
					yield return $"Error: unknown fly intention '{intentionWord}'. Expected: grind, weld, get, put, recharge, enter, place";
				if(block == null) yield return $"Error: no block at {IJK(ijk)}";

				// Only grind/weld make sense on a projection preview — it has no real inventory, power, or seats yet.
				if(intention.Value != InteractionKind.GrindWeld && IsProjection(selectedGrid.Grid))
					yield return $"Error: selected grid is a projection preview — '{intentionWord}' is not supported on it yet.";

				var eqsr = new List<EQSResult>();
				EQS.Query(block, GetEngineerCenter(), intention.Value, eqsr, 10);

				if(eqsr.Count == 0)
					yield return $"Error: no {intentionWord} interaction points found for {Name(block)} at {IJK(ijk)}";

				var cells = new List<Vector3I>();
				foreach(var r in eqsr) cells.Add(r.Cell);
				
				destinationCell = NearestToEngineer(cells);
				arrivalMessage = $"Arrived at '{intentionWord}' point for {Quote(Name(block))} at {IJK(ijk)}. Your position: {IJK(destinationCell)}.";
			}

			yield return RealFly(destinationCell, arrivalMessage, false);
		}

		internal IEnumerator Fly(ToolCall call)
		{
			string message;
			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!call.Ijk(out ijk)) yield return call.NeedIjk;

			yield return FlyTo(ijk, call.Bool("headfirst"));
		}

		private IEnumerator FlyTo(Vector3I ijk, bool headFirst)
		{
			var block = selectedGrid.GetCubeBlock(ijk);

			if(!Collisions.CenterIsFree(block, ijk))
			{
				yield return $"Destination {IJK(ijk)} is blocked by {Name(block)}. "
					+ $"Use approach if you need interact with the block.";
			}

			yield return RealFly(ijk, "", headFirst);
		}

		internal IEnumerator RealFly(Vector3I destinationCell, string arrivalMessage, bool headFirst)
		{
			var jetComp = character.Components.Get<MyCharacterJetpackComponent>();
			if(jetComp == null) yield return "Error: character has no jetpack.";
			jetComp.TurnOnJetpack(true);

			Vector3D engineer = GetEngineerCenter();
			Vector3D destination = selectedGrid.GridIntegerToWorld(destinationCell);

			Vector3I from, to;

			List<PathNode> worldPath;

			var currentGrid = GetCurrentEngineerGrid(engineer);

			if(currentGrid != null && currentGrid != selectedGrid)
			{	MyConsole.Add("Fly out of the current grid toward the target.");

				from = currentGrid.WorldToGridInteger(engineer);
				to = currentGrid.WorldToGridInteger(destination);

				aStarHelper = new AStarHelper(currentGrid, from, to);

				while(!aStarHelper.Tick()) yield return null;

				worldPath = MakePath(aStarHelper.SmoothPath(aStarHelper.GetPath()), aStarHelper);

				if(worldPath.Count == 0) yield return "No exit path found from the grid.";

				MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

				micro.Fly(worldPath);
				MarkLongFlight(engineer, worldPath);

				yield return NavigationCR(currentGrid, null, headFirst);

				MyConsole.Add("Fly out successful!");
			}

			engineer = GetEngineerCenter();

			from = selectedGrid.WorldToGridInteger(engineer);
			to = destinationCell;

			aStarHelper = new AStarHelper(selectedGrid.Grid, to, from); // Reversed: A* only knows how to find a path OUT of the grid (to border), so we search backward and reverse the result

			while(!aStarHelper.Tick()) yield return null;

			var tmp = aStarHelper.GetPath();
			tmp.Reverse(); // ! Reverse back
			worldPath = MakePath(aStarHelper.SmoothPath(tmp), aStarHelper);

			if(worldPath.Count == 0) yield return "There is no path to your destination.";

			MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

			micro.Fly(worldPath);
			MarkLongFlight(engineer, worldPath);

			yield return NavigationCR(null, arrivalMessage, headFirst);
		}

		private const double LongFlightMeters = 25;

		private void MarkLongFlight(Vector3D from, List<PathNode> path)
		{
			double length = 0;
			foreach(var node in path)
			{	length += (node.v - from).Length();
				from = node.v;
			}
			LongRunning = length > LongFlightMeters;
		}

		internal string CharacterCellText()
		{	return IJK(selectedGrid.WorldToGridInteger(GetEngineerCenter()));
		}

		internal IEnumerator NavigationCR(IMyCubeGrid exitGrid = null, string arrivalMessage = null, bool headFirst = false)
		{
			bool closeBehind = false;

			var up = CalculateUpVector(exitGrid ?? selectedGrid.Grid);

			for(;;)
			{
				var ec = GetEngineerCenter();

				// Fly-out mode: stop when the engineer has left the grid.
				if(exitGrid != null && !IsEngineerInsideGrid(ec, exitGrid))
					yield break; // no answer to LLM, continue

				if(micro.Arrived()) { yield return Success(arrivalMessage ?? $"Arrived. Position: {CharacterCellText()}"); }

				if(micro.Stuck)
				{	micro.Stop();
					yield return $"Stuck at position: {CharacterCellText()}";
				}

				if(micro.Done != null)
				{
					var open = micro.Done.openDoor;
					var close = micro.Done.closeDoor;

					if(open != null)
					{
						closeBehind = false;

						var door = GetDoorAt(open.Value);
						if(door != null && door.Status != DoorStatus.Open)
						{	
							character.MoveAndRotate(Vector3.Zero, Vector2.Zero, 0);
							
							if(door.Status != DoorStatus.Opening)
							{	var action = door.GetActionWithName("Open");
								if (action != null) action.Apply(door);

								closeBehind = true;
							}

							const double doorOpenTimeout = 5;
							var pause = Time.Now + doorOpenTimeout;

							while(Time.Now < pause)
							{	
								door = GetDoorAt(open.Value);
								if(door == null || door.Status == DoorStatus.Open) break;
						
								yield return null; // wait for door to open
							}

							if(door == null || door.Status == DoorStatus.Open)
							{}
							else
							{	yield return $"Can't open door at {IJK(open.Value)}, current position: {CharacterCellText()}";
							}
						}
					}
					if(close != null)
					{	var door = GetDoorAt(close.Value);
						if(door != null &&
							door.Status != DoorStatus.Closed &&
							door.Status != DoorStatus.Closing &&
							closeBehind)
						{	var action = door.GetActionWithName("Open"); // Open/Close
							if (action != null) action.Apply(door);
						}
					}
				}
				micro.Done = null;

				Vector2 rotation = Vector2.Zero;
				float roll = 0;

				var desiredVelocity = micro.ComputeDesiredVelocity(ec, character.Physics.LinearVelocity);

				if(headFirst || !micro.ShortSegment)
					springController.Update(ec, character.WorldMatrix.Forward, character.WorldMatrix.Up,
						micro.Target.v, up, 0.2, headFirst, out rotation, out roll);
				
				var move = micro.ComputeMoveInput(desiredVelocity, character.Physics.LinearVelocity, character.WorldMatrix);

				character.MoveAndRotate(move, rotation, roll);

				micro.DrawPath();

				yield return null; // in progress
			}
		}

		public void CharacterRotateTo(Vector3D target)
		{
			var cm = character.GetHeadMatrix(true, true);
			var center = cm.Translation;

			Vector2 rotation;
			float roll;
			springController.Update(center, cm.Forward, cm.Up,
				target, cm.Up, 0.2, false, out rotation, out roll);

			character.MoveAndRotate(Vector3.Zero, rotation, roll);
		}

		public void CharacterMove(Vector3D position, double desiredSpeed = 5.0)
		{
			var ec = GetEngineerCenter();

			Vector3D toTarget = position - ec;
			desiredSpeed = Math.Min(desiredSpeed, toTarget.Length() * 3.0);
			Vector3D desiredVelocity = toTarget.Normalized() * desiredSpeed;

			Vector3D currentVelocity = character.Physics.LinearVelocity;

			// Smooth acceleration (PD controller)
			Vector3D velocityError = desiredVelocity - currentVelocity;
			var P_coefficient = 5.0;
			Vector3D acceleration = velocityError * P_coefficient;

			var moveIndicator = micro.ComputeMoveInput(desiredVelocity, currentVelocity, character.WorldMatrix);

			character.MoveAndRotate(moveIndicator, Vector2.Zero, 0);
		}

		private static bool TryDirOffset(IMyCubeGrid grid, string s, out Vector3I offset)
		{
			offset = Vector3I.Zero;
			if(s == null) return false;

			var m = CalculateOrientation(grid);
			Vector3D worldDir;

			switch(s.ToLowerInvariant())
			{
				case "forward":  worldDir = m.Forward;  break;
				case "backward": worldDir = m.Backward; break;
				case "left":     worldDir = m.Left;     break;
				case "right":    worldDir = m.Right;    break;
				case "up":       worldDir = m.Up;       break;
				case "down":     worldDir = m.Down;     break;
				default:         return false;
			}

			var toLocal = grid.PositionComp.WorldMatrixNormalizedInv;
			offset = AxisVec(worldDir, ref toLocal);
			return true;
		}

		internal IEnumerator Fly_Direction_N(ToolCall call)
		{
			string message;
			if(!GridIsSet(out message)) yield return message;

			var dirWord = call.Str("direction");

			Vector3I offset;
			if(!TryDirOffset(selectedGrid.Grid, dirWord, out offset))
				yield return $"Error: {Quote(dirWord)} is not a direction. Expected: forward backward left right up down";

			int n;
			if(!call.Int("n", out n))
				yield return call.Need("n");
			if(n <= 0)
				yield return $"Error: n must be positive, got {n}";

			var here = selectedGrid.WorldToGridInteger(GetEngineerCenter());

			yield return FlyTo(here + offset * n, false);
		}
	}
}
