using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;

namespace LLE
{
	class AStarHelper
	{
		private IMyCubeGrid grid;
		private AStar astar;
		private const int AStarBorder = 2;

		internal List<Vector3I> GetPath()
		{
			var offset = grid.Min - AStarBorder;
			var result = new List<Vector3I>(astar.result.Count);

			for(int i = 0; i < astar.result.Count; ++i)
			{
				result.Add(astar.result[i] + offset);
			}

			return result;
		}

		internal AStarHelper(IMyCubeGrid grid_, Vector3I point_A, Vector3I point_B)
		{	grid = grid_;

			Vector3I gridSize = grid.Max - grid.Min + 1;

			LLE.Log($"RunAstar {grid.Min} {grid.Max} ({gridSize}) {point_A} -> {point_B}");

			var astarSize = gridSize + AStarBorder + AStarBorder;

			var source = new TraversabilityCalculator(grid, AStarBorder);

			astar = new AStar(astarSize, source);

			astar.Reset();

			var a = point_A - grid.Min + AStarBorder;
			var b = point_B - grid.Min + AStarBorder;
			astar.RunCalculation(a, b);
		}

		internal bool Tick()
		{	
			if (astar.Completed()) return true;

			astar.Iteration();

			return astar.Completed();
		}

		internal void DrawPath()
		{	if(astar == null) return;

			foreach(var p in astar.result)
			{	var iv = p + grid.Min - AStarBorder;
				Drawing.RoundMarker(grid.GridIntegerToWorld(iv), Color.DarkMagenta);
			}
		}
	}

	public partial class Commands
	{
		private Vector3D up;

		private readonly MicroNavigation micro = new MicroNavigation();
		private readonly DampedSpringController springController = new DampedSpringController();

		private AStarHelper aStarHelper;

		internal bool IsEngineerInsideGrid(Vector3D engineer, IMyCubeGrid grid, int border = 1)
		{
			var local = grid.WorldToGridInteger(engineer);
			return local.X >= grid.Min.X - border && local.X <= grid.Max.X + border &&
				   local.Y >= grid.Min.Y - border && local.Y <= grid.Max.Y + border &&
				   local.Z >= grid.Min.Z - border && local.Z <= grid.Max.Z + border;
		}

		internal IMyCubeGrid GetCurrentEngineerGrid(Vector3D engineer)
		{
			var sphere = new BoundingSphereD(engineer, 10);
			var entities = MyEntities.GetTopMostEntitiesInSphere(ref sphere);

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

		internal Vector3D ToWorld(Vector3I ijk)
		{	return selectedGrid.GridIntegerToWorld(ijk);
		}

		private List<PathNode> MakePath(List<Vector3I> path)
		{
			var result = new List<PathNode>();
			if (path.Count == 0) return result;

			result.Add(new PathNode() { v = ToWorld(path[0]) });
			if (path.Count == 1) return result;

			for (int i = 1; i < path.Count - 1; i++)
			{
				Vector3I prevDir = path[i] - path[i - 1];
				Vector3I nextDir = path[i + 1] - path[i];

				bool doorAhead = GetDoorAt(path[i + 1]) != null;
				bool doorBehind = GetDoorAt(path[i - 1]) != null;

				if (prevDir != nextDir || doorAhead || doorBehind)
					result.Add(new PathNode()
					{	v = ToWorld(path[i]),
						openDoor = doorAhead ? (Vector3I?)path[i + 1] : null,
						closeDoor = doorBehind ? (Vector3I?)path[i - 1] : null
					});
			}

			result.Add(new PathNode() { v = ToWorld(path[path.Count - 1]) });

			return result;
		}

		internal IEnumerator Fly(TokenParser tp)
		{
			string message;

			if(!GridIsSet(out message)) yield return message;

			Vector3I ijk;
			if(!tp.NextVector3I(out ijk)) yield return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(!Collisions.CenterIsFree(block, ijk))
			{	
				StringBuilder sb = new StringBuilder();
				sb.Append($"Destination is blocked by {Quote(Name(block))}\n");
				AppendInteractionPoints(ijk, sb);

				yield return sb.ToString();
			}

			var jetComp = character.Components.Get<MyCharacterJetpackComponent>();
			if(jetComp == null) yield return "Error: character has no jetpack component.";
			jetComp.TurnOnJetpack(true);

			Vector3D engineer = GetEngineerCenter();
			Vector3D destination = selectedGrid.GridIntegerToWorld(ijk);

			Vector3I from, to;

			List<PathNode> worldPath;

			var currentGrid = GetCurrentEngineerGrid(engineer);

			if(currentGrid != null && currentGrid != selectedGrid)
			{	MyConsole.Add("Fly out of the current grid toward the target.");

				up = currentGrid.WorldMatrix.Up;

				from = currentGrid.WorldToGridInteger(engineer);
				to = currentGrid.WorldToGridInteger(destination);

				aStarHelper = new AStarHelper(currentGrid, from, to);

				while(!aStarHelper.Tick()) yield return null;

				worldPath = MakePath(aStarHelper.GetPath());

				if(worldPath.Count == 0) yield return "There is no out path from grid.";

				MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

				micro.Fly(worldPath);

				yield return NavigationCR(currentGrid);

				MyConsole.Add("Fly out successful!");
			}

			engineer = GetEngineerCenter();

			up = selectedGrid.WorldMatrix.Up;

			from = selectedGrid.WorldToGridInteger(engineer);
			to = ijk;

			aStarHelper = new AStarHelper(selectedGrid, to, from); // Reversed: A* only knows how to find a path OUT of the grid (to border), so we search backward and reverse the result

			while(!aStarHelper.Tick()) yield return null;

			var tmp = aStarHelper.GetPath();
			tmp.Reverse(); // ! Reverse back
			worldPath = MakePath(tmp);

			if(worldPath.Count == 0) yield return "There is no path to your destination.";

			MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

			micro.Fly(worldPath);

			yield return NavigationCR();
		}

		internal string CharacterCellText()
		{	return IJK(selectedGrid.WorldToGridInteger(GetEngineerCenter()));
		}

		internal IEnumerator NavigationCR(IMyCubeGrid exitGrid = null)
		{
			bool closeBehind = false;

			for(;;)
			{
				var ec = GetEngineerCenter();

				// Fly-out mode: stop when the engineer has left the grid.
				if(exitGrid != null && !IsEngineerInsideGrid(ec, exitGrid))
					yield break; // no answer to LLM, continue

				if(micro.Arrived()) { yield return $"Arrived. Position: {CharacterCellText()}"; }

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
						if(door != null && door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Open)
						{	
							character.MoveAndRotate(Vector3.Zero, Vector2.Zero, 0);
							
							if(door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Opening)
							{	var action = door.GetActionWithName("Open");
								if (action != null) action.Apply(door);

								closeBehind = true;
							}

							var pause = Time.Now + 5;

							while(Time.Now < pause)
							{	
								door = GetDoorAt(open.Value);
								if(door == null || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open) break;
						
								yield return null; // wait for door to open
							}

							if(door == null || door.Status == Sandbox.ModAPI.Ingame.DoorStatus.Open)
							{}
							else
							{	yield return $"Can't open door at {IJK(open.Value)}, current position: {CharacterCellText()}";
							}
						}
					}
					if(close != null)
					{	var door = GetDoorAt(close.Value);
						if(door != null &&
							door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closed &&
							door.Status != Sandbox.ModAPI.Ingame.DoorStatus.Closing &&
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

				if(!micro.ShortSegment)
					springController.Update(ec, character.WorldMatrix.Forward, character.WorldMatrix.Up,
						micro.Target.v, up, 0.2, out rotation, out roll);
				
				var move = micro.ComputeMoveInput(desiredVelocity, character.Physics.LinearVelocity, character.WorldMatrix);

				character.MoveAndRotate(move, rotation, roll);

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
				target, cm.Up, 0.2, out rotation, out roll);

			character.MoveAndRotate(Vector3.Zero, rotation, roll);
		}
	}
}
