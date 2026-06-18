using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;

using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.Game.Entities.Character.Components;

namespace LLE
{
	class AStarHelper
	{
		private IMyCubeGrid grid;
		private AStar astar;
		private const int AStarBorder = 2;

		public List<Vector3D> GetPath()
		{
			List<Vector3D> path = new List<Vector3D>();

			var ar = astar.resultSimplified;

			for(int i = 0; i < ar.Count; ++i)
			{	
				var v = ar[i] + grid.Min - AStarBorder;

				path.Add(grid.GridIntegerToWorld(v));				
			}
			return path;
		}

		public AStarHelper(IMyCubeGrid grid_, Vector3I point_A, Vector3I point_B)
		{	grid = grid_;

			Vector3I gridSize = grid.Max - grid.Min + 1;

			Utilities.Log($"RunAstar {grid.Min} {grid.Max} ({gridSize}) {point_A} -> {point_B}");

			var astarSize = gridSize + AStarBorder + AStarBorder;

			var source = new TraversabilityCalculator(grid, AStarBorder);

			if (astar == null || astar.Size != astarSize)
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

		internal bool IsEngineerInsideGrid(Vector3D engineer, IMyCubeGrid grid)
		{
			var local = grid.WorldToGridInteger(engineer);
			return local.X >= grid.Min.X - 1 && local.X <= grid.Max.X + 1 &&
				   local.Y >= grid.Min.Y - 1 && local.Y <= grid.Max.Y + 1 &&
				   local.Z >= grid.Min.Z - 1 && local.Z <= grid.Max.Z + 1;
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
				sb.Append($"Error: Destination is blocked by {Quote(Name(block))}, nearest free space is:\n");

				AppendNearbyFreeCells(ijk, sb);

				yield return sb.ToString();
			}

			var jetComp = character.Components.Get<MyCharacterJetpackComponent>();
			jetComp.TurnOnJetpack(true);

			Vector3D engineer = Utilities.GetEngineerCenter(character);
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

				worldPath = aStarHelper.GetPath();

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

					engineer = Utilities.GetEngineerCenter(character);

					if(!IsEngineerInsideGrid(engineer, currentGrid))
					{	MyConsole.Add("Fly out successfull!");
						break;
					}
				}
			}

			engineer = Utilities.GetEngineerCenter(character);

			up = selectedGrid.WorldMatrix.Up;

			from = selectedGrid.WorldToGridInteger(engineer);
			to = ijk;

			aStarHelper = new AStarHelper(selectedGrid, to, from); // Reversed: A* only knows how to find a path OUT of the grid (to border), so we search backward and reverse the result

			while(!aStarHelper.Tick()) yield return null;

			worldPath = aStarHelper.GetPath();
			worldPath.Reverse(); // ! Reverse back

			if(worldPath.Count == 0) yield return "There is no path to your destination.";

			MyConsole.Add($"path.Count {worldPath.Count}", Color.IndianRed);

			micro.Fly(worldPath);

			for(;;)
			{	yield return NavigationStep(selectedGrid);
			}
		}

		internal string CharacterCellText()
		{	Vector3D e = Utilities.GetEngineerCenter(character);
			return IJK(selectedGrid.WorldToGridInteger(e));
		}

		internal string NavigationStep(IMyCubeGrid grid)
		{
			if(micro.Arrived()) return $"Arrived. Position: {CharacterCellText()}";

			if(MyAPIGateway.Input.IsNewLeftMousePressed() ||
				MyAPIGateway.Input.IsNewRightMousePressed())
			{	micro.Stop();
				return $"Cancelled by user. Current position: {CharacterCellText()}";
			}
			
			if(micro.Stuck)
			{	micro.Stop();
				return $"Stuck at position: {CharacterCellText()}";
			}

			var ec = Utilities.GetEngineerCenter(character);

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
			var cm = character.GetHeadMatrix(false, false);
			var center = cm.Translation;

			Vector2 rotation;
			float roll;
			springController.Update(center, cm.Forward, cm.Up,
				target, cm.Up, 0.2, out rotation, out roll);

			character.MoveAndRotate(Vector3.Zero, rotation, roll);
		}
	}
}
