using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace LLE
{
	public static class Navigation
	{
		private static MicroNavigation micro = new MicroNavigation();
		private static DampedSpringController springController = new DampedSpringController();
		private static Vector3D up;
		
		internal static void FlyToGrid(IMyCharacter ch, IMyCubeGrid largeGrid, Vector3I toI)
		{
			up = largeGrid.WorldMatrix.Up;

			Vector3D from = Utilities.GetEngineerCenter(ch);
			Vector3D to = largeGrid.GridIntegerToWorld(toI);

			double dist;
			IMySlimBlock slimBlock;
			LineD line = new LineD(from, to);
			largeGrid.GetLineIntersectionExactAll(ref line, out dist, out slimBlock);

			if (slimBlock == null)
			{	List<Vector3D> path = new List<Vector3D>();
				path.Add(from);
				path.Add(to);
				micro.Fly(path);
				return;
			}

			MyConsole.Add("No direct path to destination point", Color.Red);
		}

		internal static void Update(IMyCharacter ch)
		{
			if(micro.Arrived()) return;

			bool mouse = MyAPIGateway.Input.IsNewLeftMousePressed() ||
				MyAPIGateway.Input.IsNewRightMousePressed();

			if(mouse)
			{	micro.Stop();
				MyConsole.Add("Navigation: Cancelled", Color.Red);
				return;
			}
			
			if(micro.Arrived())
			{	MyConsole.Add("Navigation: Arrived", Color.BlueViolet);
				return;
			}
			
			if(micro.Stuck)
			{	micro.Stop();
				MyConsole.Add("Navigation: Stuck", Color.DarkRed);
				return;
			}

			var ec = Utilities.GetEngineerCenter(ch);

			Vector2 rotation = Vector2.Zero;
			float roll = 0;

			if(!micro.ShortSegment)
				springController.Update(ec, ch.WorldMatrix.Forward, ch.WorldMatrix.Up,
					micro.currentTargetPoint, up, 0.2, out rotation, out roll);

			var desiredVelocity = micro.ComputeDesiredVelocity(ec, ch.Physics.LinearVelocity);
			var move = micro.ComputeMoveInput(desiredVelocity, ch.Physics.LinearVelocity, ch.WorldMatrix);
			
			ch.MoveAndRotate(move, rotation, roll);
		}

/*		AStar astar;
		const int AStarBorder = 1;

		public void Update()
		{
			var pm = ch.GetHeadMatrix(false);

			

			
			else if (mouse && grid_A == grid_B && grid_A != null)
			{
				RunAstar(grid_A);
			}

			if (astar != null && !astar.Completed())
			{	astar.Iteration();

				if(astar.Completed())
				{
					var grid = grid_A;

					List<Vector3D> path = new List<Vector3D>();

					path.Add(Utilities.GetEngineerCenter(ch));

					for(int i = 0; i < astar.result.Count; ++i)
					{	
						var v = astar.result[i] + grid.Min - AStarBorder;

						path.Add(grid.GridIntegerToWorld(v));				
					}
	
					navigationActive = true;
					navigation.Fly(path);
				}
			}
		}

		private void RunAstar(IMyCubeGrid grid)
		{
			Vector3I gridSize = grid.Max - grid.Min + 1;

			Log($"RunAstar {grid.Min} {grid.Max} {gridSize}");

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

		private void DrawTraversability(IMySlimBlock block)
		{
			if(astar == null) return;

			Traversability trav = astar.GetTraversability(block.Position - grid_A.Min + AStarBorder);

			var zero = grid_A.GridIntegerToWorld(selectedBlock);

			var dirs = Constants.SixDirections;

			for (int d = 0; d < dirs.Length; ++d)
			{
				Vector3I dir = dirs[d];
				var world = (grid_A.GridIntegerToWorld(selectedBlock + dir) - zero) * 0.5 + zero;
				Drawing.RoundMarker(world, trav[dirs[d]] ? Color.Gray : Color.Lime);
			}
			Drawing.RoundMarker(zero, trav[0,0,0] ? Color.Black : Color.Green);
		}
*/
	}
}