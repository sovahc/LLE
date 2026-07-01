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

		internal IMyDoor GetDoorAt(Vector3I ijk)
		{
			var block = grid?.GetCubeBlock(ijk);
			return block?.FatBlock as IMyDoor;
		}

		private List<Vector3I> SimplifyPath(List<Vector3I> path)
		{
			if (path.Count <= 2)
				return path;

			var simplified = new List<Vector3I>();
			simplified.Add(path[0]);

			for (int i = 1; i < path.Count - 1; i++)
			{
				Vector3I prevDir = path[i] - path[i - 1];
				Vector3I nextDir = path[i + 1] - path[i];

				bool door =
					GetDoorAt(path[i - 1]) != null ||
					GetDoorAt(path[i + 0]) != null ||
					GetDoorAt(path[i + 1]) != null;

				if (prevDir != nextDir || door)
					simplified.Add(path[i]);
			}

			simplified.Add(path[path.Count - 1]);
			return simplified;
		}

		public List<Vector3D> GetSimplePath()
		{
			var ar = astar.result;

			for(int i = 0; i < ar.Count; ++i)
			{	
				ar[i] += grid.Min - AStarBorder;
			}

			ar = SimplifyPath(ar);

			List<Vector3D> path = new List<Vector3D>();
			foreach(var v in ar)
			{	path.Add(grid.GridIntegerToWorld(v));
			}

			return path;
		}

		public AStarHelper(IMyCubeGrid grid_, Vector3I point_A, Vector3I point_B)
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

		public bool Tick()
		{	
			if (astar.Completed()) return true;

			astar.Iteration();

			if (!astar.Completed()) return false;

			return true;
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
			jetComp.TurnOnJetpack(true);

			Vector3D engineer = GetEngineerCenter();
			Vector3D destination = selectedGrid.GridIntegerToWorld(ijk);

			Vector3I from, to;

			List<Vector3D> worldPath;

			var currentGrid = GetCurrentEngineerGrid(engineer);

			if(currentGrid != null && currentGrid != selectedGrid)
			{	MyConsole.Add("Fly out of the current grid toward the target");

				up = currentGrid.WorldMatrix.Up;

				from = currentGrid.WorldToGridInteger(engineer);
				to = currentGrid.WorldToGridInteger(destination);

				aStarHelper = new AStarHelper(currentGrid, from, to);

				while(!aStarHelper.Tick()) yield return null;

				worldPath = aStarHelper.GetSimplePath();

				if(worldPath.Count == 0) yield return "There is no out path from grid";

				MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

				micro.Fly(worldPath);

				for(;;)
				{	var r = NavigationStep(currentGrid);
					if(r != null)
					{	MyConsole.Add($"Fly out: {r}");
						break;
					}
					
					yield return null;

					engineer = GetEngineerCenter();

					if(!IsEngineerInsideGrid(engineer, currentGrid))
					{	MyConsole.Add("Fly out successfull!");
						break;
					}
				}
			}

			engineer = GetEngineerCenter();

			up = selectedGrid.WorldMatrix.Up;

			from = selectedGrid.WorldToGridInteger(engineer);
			to = ijk;

			aStarHelper = new AStarHelper(selectedGrid, to, from); // Reversed: A* only knows how to find a path OUT of the grid (to border), so we search backward and reverse the result

			while(!aStarHelper.Tick()) yield return null;

			worldPath = aStarHelper.GetSimplePath();
			worldPath.Reverse(); // ! Reverse back

			if(worldPath.Count == 0) yield return "There is no path to your destination.";

			MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

			micro.Fly(worldPath);

			for(;;)
			{	yield return NavigationStep(selectedGrid);
			}
		}

		internal string CharacterCellText()
		{	return IJK(selectedGrid.WorldToGridInteger(GetEngineerCenter()));
		}

		internal string NavigationStep(IMyCubeGrid grid)
		{
			if(micro.Arrived()) return $"Arrived. Position: {CharacterCellText()}";

			if(micro.Stuck)
			{	micro.Stop();
				return $"Stuck at position: {CharacterCellText()}";
			}

			var ec = GetEngineerCenter();

			Vector2 rotation = Vector2.Zero;
			float roll = 0;

			if(!micro.ShortSegment)
				springController.Update(ec, character.WorldMatrix.Forward, character.WorldMatrix.Up,
					micro.currentTargetPoint, up, 0.2, out rotation, out roll);

			var desiredVelocity = micro.ComputeDesiredVelocity(ec, character.Physics.LinearVelocity);
			var move = micro.ComputeMoveInput(desiredVelocity, character.Physics.LinearVelocity, character.WorldMatrix);
			
			character.MoveAndRotate(move, rotation, roll);

			return null; // In progress
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
