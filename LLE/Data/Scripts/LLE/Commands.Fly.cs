using System.Collections;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using System;

using DoorStatus = Sandbox.ModAPI.Ingame.DoorStatus;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace LLE
{
	public partial class Commands
	{
		private readonly MicroNavigation micro = new MicroNavigation();
		private readonly DampedSpringController springController = new DampedSpringController();

		private AStarHelper aStarHelper;

		internal bool IsInsideGrid(Vector3D point, IMyCubeGrid grid)
		{
			int border = 0;
			if(grid.GridSizeEnum == MyCubeSize.Large) border = 1;
			if(grid.GridSizeEnum == MyCubeSize.Small) border = 6;

			var local = grid.WorldToGridInteger(point);
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

				if(!IsInsideGrid(engineer, g)) continue;

				double distanceSq = (engineer - g.PositionComp.WorldAABB.Center).LengthSquared();
				if(distanceSq > minimalDistanceSq) continue;

				minimalDistanceSq = distanceSq;
				result = g;
			}

			return result;
		}

		// The same test A* applies to its endpoints, so a surviving cell is one the search will accept.
		private void KeepNavigableCells(List<Vector3I> cells)
		{
			var source = new TraversabilityCalculator(selectedGrid, 0);

			var box = BoundingBoxD.CreateInvalid();
			foreach(var c in cells) box.Include(selectedGrid.GridIntegerToWorld(c));
			box.Inflate(selectedGrid.GridSize + 2.0);

			var voxels = new List<Sandbox.Game.Entities.MyVoxelBase>();
			var grids = new List<IMyCubeGrid>();
			source.QueryObstacles(box, voxels, grids);

			for(int i = cells.Count - 1; i >= 0; --i)
				if(source.GetTraversability(cells[i], voxels, grids).Center)
					cells.RemoveAt(i);
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

			if (path.Count == 1)
			{	result.Add(new PathNode() { v = helper.CellToWorld(path[0]) });
				return result;
			}

			// path[0] is skipped: the engineer stands there already, and its center may be inside the block he stands on.
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

		// Pure read: the cell to stand in for this interaction, nearest to the engineer.
		private Vector3I? InteractionCell(IMySlimBlock block, InteractionKind kind)
		{
			var eqsr = new List<EQSResult>();
			EQS.Query(block, GetEngineerCenter(), kind, eqsr, 10);

			if(eqsr.Count == 0) return null;

			var cells = new List<Vector3I>();
			foreach(var r in eqsr) cells.Add(ToSelectedGrid(block.CubeGrid, r.Cell));

			return NearestToEngineer(cells);
		}

		// The interaction point nearest to where the engineer stands now — after a flight that is the one
		// he flew to. Positions here are world space, so a projection block needs no cell conversion.
		private EQSResult? NearestInteractionPoint(IMySlimBlock block, InteractionKind kind)
		{
			var eqsr = new List<EQSResult>();
			EQS.Query(block, GetEngineerCenter(), kind, eqsr, 10);

			if(eqsr.Count == 0) return null;

			var engineer = GetEngineerCenter();
			var best = eqsr[0];

			foreach(var r in eqsr)
				if(Vector3D.DistanceSquared(engineer, r.chPosition)
					< Vector3D.DistanceSquared(engineer, best.chPosition)) best = r;

			return best;
		}

		// Every action tool flies to its own interaction point, so the model spends no turn on approach.
		// The nearest free point decides: standing on it means no flight, and CharacterMoveCR covers the
		// last metre. Asking whether we are already at an interaction point cannot decide it — on a small
		// grid that query ignores the cell it is given and answers yes from anywhere.
		private IEnumerator ReachCR(IMySlimBlock block, InteractionKind kind)
		{
			var cell = InteractionCell(block, kind);
			if(cell == null) yield return E_UNREACHABLE;

			var distance = Vector3D.Distance(GetEngineerCenter(), selectedGrid.GridIntegerToWorld(cell.Value));
			if(distance <= Constants.MaxInteractionDistance) yield break;

			yield return RealFly(cell.Value, null, false, true);
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

			var block = BlockAt(ijk);

			Vector3I destinationCell;
			string arrivalMessage;

			if(string.Equals(intentionWord, "place", StringComparison.OrdinalIgnoreCase))
			{
				var occupant = selectedGrid.GetCubeBlock(ijk);
				if(occupant != null)
					yield return $"Error: {IJK(ijk)} is occupied by block {Quote(Name(occupant))}. Cannot build a block on an occupied cell.";

				var producer = EQS.ProduceCells(selectedGrid, ijk, GetEngineerCenter());
				var placeCells = new List<Vector3I>();
				
				foreach (var c in producer)
				{	if(c == ijk) continue;
					placeCells.Add(c);
				}

				KeepNavigableCells(placeCells);

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
				if(intention.Value != InteractionKind.GrindWeld && IsProjection(block.CubeGrid))
					yield return $"Error: {IJK(ijk)} is not built yet — '{intentionWord}' needs a real block.";

				var cell = InteractionCell(block, intention.Value);
				if(cell == null)
					yield return $"Error: no {intentionWord} interaction points found for {Name(block)} at {IJK(ijk)}";

				destinationCell = cell.Value;
				arrivalMessage = $"Arrived at '{intentionWord}' point for {Quote(Name(block))} at {IJK(ijk)}. Your position: {IJK(destinationCell)}.";
			}

			yield return Validated;

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

		internal IEnumerator RealFly(Vector3I destinationCell, string arrivalMessage, bool headFirst,
			bool silent = false)
		{
			var jetComp = character.Components.Get<MyCharacterJetpackComponent>();
			if(jetComp == null) yield return "Error: character has no jetpack.";

			yield return Validated;

			jetComp.TurnOnJetpack(true);

			Vector3D engineer = GetEngineerCenter();
			Vector3D destination = selectedGrid.GridIntegerToWorld(destinationCell);

			Vector3I from, to;

			List<PathNode> worldPath;

			var currentGrid = GetCurrentEngineerGrid(engineer);

			if(currentGrid != null && currentGrid != selectedGrid && !IsInsideGrid(engineer, selectedGrid))
			{	MyConsole.Add("Fly out of the current grid toward the target.");

				from = currentGrid.WorldToGridInteger(engineer);
				to = currentGrid.WorldToGridInteger(destination);

				aStarHelper = new AStarHelper(currentGrid, from, to, from);

				while(!aStarHelper.Tick()) yield return null;

				worldPath = MakePath(aStarHelper.SmoothPath(aStarHelper.GetPath()), aStarHelper);

				if(worldPath.Count == 0) yield return "No exit path found from the grid.";

				MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

				micro.Fly(worldPath);

				yield return NavigationCR(currentGrid, null, headFirst);

				MyConsole.Add("Fly out successful!");
			}

			engineer = GetEngineerCenter();

			from = selectedGrid.WorldToGridInteger(engineer);
			to = destinationCell;

			aStarHelper = new AStarHelper(selectedGrid, to, from, from); // Reversed: A* only knows how to find a path OUT of the grid (to border), so we search backward and reverse the result

			while(!aStarHelper.Tick()) yield return null;

			var tmp = aStarHelper.GetPath();
			tmp.Reverse(); // ! Reverse back
			worldPath = MakePath(aStarHelper.SmoothPath(tmp), aStarHelper);

			if(worldPath.Count == 0) yield return "There is no path to your destination.";

			MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

			micro.Fly(worldPath);

			yield return NavigationCR(null, arrivalMessage, headFirst, silent);
		}

		internal string CharacterCellText()
		{	return IJK(selectedGrid.WorldToGridInteger(GetEngineerCenter()));
		}

		internal IEnumerator NavigationCR(IMyCubeGrid exitGrid = null, string arrivalMessage = null,
			bool headFirst = false, bool silent = false)
		{
			bool closeBehind = false;

			var up = CalculateUpVector(exitGrid ?? selectedGrid);

			for(;;)
			{
				var ec = GetEngineerCenter();

				// Fly-out mode: stop when the engineer has left the grid.
				if(exitGrid != null && !IsInsideGrid(ec, exitGrid))
					yield break; // no answer to LLM, continue

				if(micro.Arrived())
				{	// Flying out is only half of the trip; answering here would end the command before it starts.
					// The same holds for a flight another command started to reach its own target.
					if(exitGrid != null || silent) yield break;

					yield return Success(arrivalMessage ?? $"Arrived. Position: {CharacterCellText()}");
				}

				if(micro.Stuck)
				{	var back = Vector3D.Normalize(ec - micro.Target.v);
					micro.Stop();

					var point = ec + back * 2.5;

					// Backing off is best-effort: the two answers below say how far it got.
					yield return CharacterMoveCR(point, false);

					character.MoveAndRotate(Vector3.Zero, Vector2.Zero, 0);

					yield return Vector3D.Distance(GetEngineerCenter(), ec) > 1.0
						? $"The flight was interrupted: you got stuck and backed off to {CharacterCellText()}."
						: $"The flight was interrupted: you are stuck at {CharacterCellText()} and could not back off. Try unstuck.";
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

		internal IEnumerator CharacterMoveCR(Vector3D position, bool required = true)
		{
			const int budget = 90; // 1.5 s at 60 ticks
			const double arrivalDistance = 0.2;
			const double arrivalSpeed = 0.2;

			double distance = 0;

			for(int tick = 0; tick < budget; ++tick)
			{
				distance = Vector3D.Distance(GetEngineerCenter(), position);

				if(distance < arrivalDistance
					&& character.Physics.LinearVelocity.LengthSquared() < arrivalSpeed * arrivalSpeed)
					yield break;

				CharacterMove(position);
				yield return null;
			}

			MyConsole.Add($"CharacterMoveCR: {distance:F2} m short after {budget} ticks", Color.Red);

			// Running out of budget is not failing to arrive: the loop also waits for the drift to settle,
			// and standing within a body length of the spot is inside the reach of every tool.
			if(required && distance > Constants.EngineerCapsuleHeight)
				yield return $"Error: could not take position, {distance:F1} m short — something is in the way."
					+ " Try 'points' for another side of the block, or 'unstuck'.";
		}

		internal IEnumerator CharacterRotateCR(Vector3D target)
		{
			const int budget = 90;
			const double aimDot = 0.999; // ~2.5 degrees

			double aim = 0;

			for(int tick = 0; tick < budget; ++tick)
			{
				var head = character.GetHeadMatrix(true, true);
				aim = Vector3D.Dot(head.Forward, Vector3D.Normalize(target - head.Translation));

				if(aim > aimDot) yield break;

				CharacterRotateTo(target);
				yield return null;
			}

			MyConsole.Add($"CharacterRotateCR: aim {aim:F3} after {budget} ticks", Color.Red);
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

		private double FreeDistance(Vector3D from, Vector3D direction, double length, List<IHitInfo> hits)
		{
			hits.Clear();
			MyAPIGateway.Physics.CastRay(from, from + direction * length, hits, CollisionLayers.CharacterCollisionLayer);

			var nearest = length;

			foreach(var hit in hits)
			{
				var entity = hit.HitEntity;
				if(entity == null || entity == character) continue;
				if(entity == character.EquippedTool) continue;

				nearest = Math.Min(nearest, hit.Fraction * length);
			}

			return nearest;
		}

		private void MoveHeadFirst(Vector3D point, Vector3D up, bool thrust)
		{
			var ec = GetEngineerCenter();

			Vector2 rotation;
			float roll;
			springController.Update(ec, character.WorldMatrix.Forward, character.WorldMatrix.Up,
				point, up, 0.2, true, out rotation, out roll);

			var move = Vector3.Zero;

			if(thrust)
			{	var desiredVelocity = Vector3D.Normalize(point - ec) * 5.0;
				move = micro.ComputeMoveInput(desiredVelocity, character.Physics.LinearVelocity, character.WorldMatrix);
			}

			character.MoveAndRotate(move, rotation, roll);
		}

		internal IEnumerator Unstuck()
		{
			string message;
			if(!GridIsSet(out message)) yield return message;

			var jetComp = character.Components.Get<MyCharacterJetpackComponent>();
			if(jetComp == null) yield return "Error: character has no jetpack.";

			yield return Validated;

			jetComp.TurnOnJetpack(true);

			const double probeLength = 6.0;
			const double minimalGap = 1.2;
			const int maximalAttempts = 6;

			var start = GetEngineerCenter();
			var m = CalculateOrientation(selectedGrid);
			var up = CalculateUpVector(selectedGrid);

			var hits = new List<IHitInfo>();
			var candidates = new List<KeyValuePair<double, Vector3D>>();

			Vector3I d;
			for(d.Z = -1; d.Z <= 1; ++d.Z)
				for(d.Y = -1; d.Y <= 1; ++d.Y)
					for(d.X = -1; d.X <= 1; ++d.X)
					{
						if(d == Vector3I.Zero) continue;

						var direction = Vector3D.Normalize(d.X * m.Right + d.Y * m.Up + d.Z * m.Forward);
						var free = FreeDistance(start, direction, probeLength, hits);

						if(free >= minimalGap)
							candidates.Add(new KeyValuePair<double, Vector3D>(free, direction));
					}

			if(candidates.Count == 0)
				yield return $"Error: you are wedged in — every direction is blocked within {minimalGap} m.";

			candidates.Sort((a, b) => b.Key.CompareTo(a.Key));

			var attempts = Math.Min(candidates.Count, maximalAttempts);

			// A ray is thinner than the engineer, so an open direction may still not let him through.
			for(int i = 0; i < attempts; ++i)
			{
				var direction = candidates[i].Value;
				var point = start + direction * (candidates[i].Key - 0.5);

				// The engineer is long and thin: a gap that fits him lengthwise will not fit him sideways.
				SetPause(Constants.MicronavigationDelay);
				while(IsPaused())
				{	if(Vector3D.Dot(character.WorldMatrix.Up, direction) > 0.95) break;
					MoveHeadFirst(point, up, false);
					yield return null;
				}

				SetPause(Constants.MicronavigationDelay);
				while(IsPaused())
				{	if(Vector3D.DistanceSquared(GetEngineerCenter(), point) < 1.0) break;
					MoveHeadFirst(point, up, true);
					yield return null;
				}

				character.MoveAndRotate(Vector3.Zero, Vector2.Zero, 0);

				var moved = Vector3D.Distance(GetEngineerCenter(), start);
				if(moved > 1.0)
					yield return Success($"Broke free, moved {moved:F1} m. Position: {CharacterCellText()}");
			}

			yield return $"Error: could not break free — tried {attempts} open directions, the engineer does not move.";
		}
	}
}
