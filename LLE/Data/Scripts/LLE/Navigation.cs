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

		internal bool Step()
		{
			if (astar != null && !astar.Completed())
			{	
				astar.Iteration();

				if(astar.Completed())
				{
					List<Vector3D> path = new List<Vector3D>();

					path.Add(Utilities.GetEngineerCenter(character));

					for(int i = 0; i < astar.result.Count; ++i)
					{	
						var v = astar.result[i] + grid.Min - AStarBorder;

						path.Add(grid.GridIntegerToWorld(v));				
					}
	
					micro.Fly(path);
				}

				return true; // "thinking"
			}

			if(micro.Arrived()) return false;

			bool mouse = MyAPIGateway.Input.IsNewLeftMousePressed() ||
				MyAPIGateway.Input.IsNewRightMousePressed();

			if(mouse)
			{	micro.Stop();
				MyConsole.Add("Navigation: Cancelled", Color.Red);
				return false;
			}
			
			if(micro.Arrived())
			{	MyConsole.Add("Navigation: Arrived", Color.BlueViolet);
				return false;
			}
			
			if(micro.Stuck)
			{	micro.Stop();
				MyConsole.Add("Navigation: Stuck", Color.DarkRed);
				return false;
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

			return true;
		}

		private void RunAstar(Vector3I point_A, Vector3I point_B)
		{
			Vector3I gridSize = grid.Max - grid.Min + 1;

			Utilities.Log($"RunAstar {grid.Min} {grid.Max} ({gridSize}) {point_A} -> {point_B}");

			var astarSize = gridSize + AStarBorder + AStarBorder;

			if (astar == null || astar.Size != astarSize) astar = new AStar(astarSize);

			List<IMySlimBlock> blocks = new List<IMySlimBlock>();
			grid.GetBlocks(blocks);

			using (var prof = new Profiler("fill"))
			{
				astar.Reset(true);

				int unknownBlocks = 0;
				foreach (var slim in blocks)
				{
					var p = slim.Position - grid.Min + AStarBorder;

					Traversability t;
					if (Collisions._traversabilityCache.TryGetValue(slim.BlockDefinition.Id, out t))
					{
						var Min = slim.Min;
						var Max = slim.Max;
						Vector3I v;

						if (Min == Max)
						{
							MatrixI m = new MatrixI(slim.Orientation);

							Vector3I v2;
							Traversability t2 = new Traversability();

							for (v.Z = -1; v.Z <= 1; ++v.Z)
								for (v.Y = -1; v.Y <= 1; ++v.Y)
									for (v.X = -1; v.X <= 1; ++v.X)
									{
										Vector3I.TransformNormal(ref v, ref m, out v2);
										t2[v2] = t[v];
									}
							astar.SetTraversability(p, t2);
						}
						else
						{
							for (v.Z = Min.Z; v.Z <= Max.Z; ++v.Z)
								for (v.Y = Min.Y; v.Y <= Max.Y; ++v.Y)
									for (v.X = Min.X; v.X <= Max.X; ++v.X)
										astar.SetTraversability(v - grid.Min + AStarBorder, Traversability.Blocked);
						}
					}
					else
					{
						astar.SetTraversability(p, Traversability.Blocked);
						++unknownBlocks;
					}
				}
				MyConsole.Add($"unknownBlocks {unknownBlocks}", Color.Yellow);
				MyConsole.Add($"{prof}", Color.IndianRed);
			}

			var a = point_A - grid.Min + AStarBorder;
			var b = point_B - grid.Min + AStarBorder;
			astar.RunCalculation(a, b);
		}

		private void DrawTraversability(IMySlimBlock block, Vector3I v)
		{
			if(astar == null) return;

			Traversability trav = astar.GetTraversability(block.Position - grid.Min + AStarBorder);

			var zero = grid.GridIntegerToWorld(v);

			var dirs = Constants.SixDirections;

			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				var world = (grid.GridIntegerToWorld(v + dir) - zero) * 0.5 + zero;
				Drawing.RoundMarker(world, trav[dirs[d]] ? Color.Gray : Color.Lime);
			}
			Drawing.RoundMarker(zero, trav[0,0,0] ? Color.Black : Color.Green);
		}
	}
}