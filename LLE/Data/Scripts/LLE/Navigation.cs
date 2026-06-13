using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LLE
{
	public class Navigation
	{
		private readonly MicroNavigation micro = new MicroNavigation();
		private readonly DampedSpringController springController = new DampedSpringController();
		private Vector3D up;
		private readonly IMyCharacter character;

		private IMyCubeGrid grid;
		private AStar astar;
		private const int AStarBorder = 1;
		private bool isTest;

		public Navigation(IMyCharacter character)
		{	this.character = character;
		}

		internal string CharacterCellText()
		{	Vector3D e = Utilities.GetEngineerCenter(character);
			return Commands.IJK(grid.WorldToGridInteger(e));
		}
		
		internal void FlyInsideGrid(IMyCubeGrid largeGrid, Vector3I toI)
		{
			isTest = false;

			grid = largeGrid;

			up = grid.WorldMatrix.Up;

			Vector3D from = Utilities.GetEngineerCenter(character);
			Vector3D to = grid.GridIntegerToWorld(toI);

			// try direct path to point

			/*double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(from, to);
			grid.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null)
			{	List<Vector3D> path = new List<Vector3D>();
				path.Add(from);
				path.Add(to);
				micro.Fly(path);
				return;
			}*/

			// run A*

			var fromI = grid.WorldToGridInteger(from);

			RunAstar(fromI, toI);
		}

		internal string Step()
		{
			if (astar != null && !astar.Completed())
			{	
				astar.Iteration();

				if(astar.Completed())
				{
					var ar = astar.resultSimplifyed;

					if(ar.Count == 0)
						return "There is no path to your destination.";

					List<Vector3D> path = new List<Vector3D>();

					path.Add(Utilities.GetEngineerCenter(character));

					for(int i = 0; i < ar.Count; ++i)
					{	
						var v = ar[i] + grid.Min - AStarBorder;

						path.Add(grid.GridIntegerToWorld(v));				
					}

					MyConsole.Add($"path.Count {path.Count}", Color.IndianRed);
					micro.Fly(path);
				}
				return null; // "thinking"
			}

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

		public void JustRotateTo(Vector3D target)
		{
			var cm = character.GetHeadMatrix(true);
			var center = cm.Translation;

			Vector2 rotation;
			float roll;
			springController.Update(center, cm.Forward, cm.Up,
				target, cm.Up, 0.2, out rotation, out roll);

			character.MoveAndRotate(Vector3.Zero, rotation, roll);
		}

		public void RunAstar(Vector3I point_A, Vector3I point_B)
		{
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

		internal void TestAstar(IMyCubeGrid selectedGrid, Vector3I a, Vector3I b)
		{
			isTest = true;
			grid = selectedGrid;
			RunAstar(a, b);
		}

		internal void TestAstarStep()
		{	if(isTest && astar != null) astar.Iteration();
		}

		internal void DrawPath()
		{	if(astar == null) return;
			foreach(var p in astar.result)
			{	var iv = p + grid.Min - AStarBorder;
				Drawing.RoundMarker(grid.GridIntegerToWorld(iv), Color.DarkMagenta);
			}
		}
	}
}