using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace LLE
{
	public class Navigation
	{
		private MicroNavigation micro = new MicroNavigation();
		private DampedSpringController springController = new DampedSpringController();
		private Vector3D up;
		private IMyCharacter character;

		IMyCubeGrid grid;
		private AStar astar;
		private const int AStarBorder = 1;

		public Navigation(IMyCharacter character)
		{	this.character = character;
		}

		internal Vector3I CharacterCell()
		{	Vector3D e = Utilities.GetEngineerCenter(character);
			return grid.WorldToGridInteger(e);
		}
		
		internal void FlyInsideGrid(IMyCubeGrid largeGrid, Vector3I toI)
		{
			grid = largeGrid;

			up = grid.WorldMatrix.Up;

			Vector3D from = Utilities.GetEngineerCenter(character);
			Vector3D to = grid.GridIntegerToWorld(toI);

			// try direct path to point

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(from, to);
			grid.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null)
			{	List<Vector3D> path = new List<Vector3D>();
				path.Add(from);
				path.Add(to);
				micro.Fly(path);
				return;
			}

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
					if(astar.result.Count == 0)
						return "There is no path to your destination.";

					List<Vector3D> path = new List<Vector3D>();

					path.Add(Utilities.GetEngineerCenter(character));

					for(int i = 0; i < astar.result.Count; ++i)
					{	
						var v = astar.result[i] + grid.Min - AStarBorder;

						path.Add(grid.GridIntegerToWorld(v));				
					}

					MyConsole.Add($"path.Count {path.Count}", Color.IndianRed);
					micro.Fly(path);
				}
				return null; // "thinking"
			}

			if(micro.Arrived()) return $"Arrived. Position: {CharacterCell()}";

			if(MyAPIGateway.Input.IsNewLeftMousePressed() ||
				MyAPIGateway.Input.IsNewRightMousePressed())
			{	micro.Stop();
				return $"Cancelled by user. Current position: {CharacterCell()}";
			}
			
			if(micro.Stuck)
			{	micro.Stop();
				return $"Stuck at position: {CharacterCell()}";
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

		private void RunAstar(Vector3I point_A, Vector3I point_B)
		{
			Vector3I gridSize = grid.Max - grid.Min + 1;

			Utilities.Log($"RunAstar {grid.Min} {grid.Max} ({gridSize}) {point_A} -> {point_B}");

			var astarSize = gridSize + AStarBorder + AStarBorder;

			TraversabilitySource source = new TraversabilitySource((index) =>
			{
				Vector3I pos;
				int strideXY = astarSize.X * astarSize.Y;
				pos.Z = index / strideXY;
				index -= pos.Z * strideXY;
				pos.Y = index / astarSize.X;
				pos.X = index - pos.Y * astarSize.X;

				var gridPos = pos - AStarBorder;

				if (gridPos.X < grid.Min.X || gridPos.Y < grid.Min.Y || gridPos.Z < grid.Min.Z ||
			    	gridPos.X > grid.Max.X || gridPos.Y > grid.Max.Y || gridPos.Z > grid.Max.Z)
					return Traversability.Free;

				var slim = grid.GetCubeBlock(gridPos) as IMySlimBlock;
				if (slim == null)
					return Traversability.Free;

				Traversability t;
				if (!Collisions._traversabilityCache.TryGetValue(slim.BlockDefinition.Id, out t))
					return Traversability.Blocked;

				var min = slim.Min;
				var max = slim.Max;
				if (min == max)
				{
					Vector3I localPos = gridPos - min;
					if (localPos.X < -1 || localPos.X > 1 || localPos.Y < -1 || localPos.Y > 1 || localPos.Z < -1 || localPos.Z > 1)
						return Traversability.Free;

					MatrixI m = new MatrixI(slim.Orientation);
					return Traversability.Rotate(t, m);
				}
				return Traversability.Blocked;
			});

			if (astar == null || astar.Size != astarSize)
				astar = new AStar(astarSize, source);

			astar.Reset();

			var a = point_A - grid.Min + AStarBorder;
			var b = point_B - grid.Min + AStarBorder;
			astar.RunCalculation(a, b);
		}
	}
}