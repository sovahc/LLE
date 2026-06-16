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
	public partial class Commands
	{
        private Vector3D up;
        
        private AStar astar;
		private const int AStarBorder = 1;

        private readonly MicroNavigation micro = new MicroNavigation();
		private readonly DampedSpringController springController = new DampedSpringController();

        internal bool IsEngineerInsideGrid(IMyCubeGrid grid)
		{
			var pos = Utilities.GetEngineerCenter(character);
			var local = grid.WorldToGridInteger(pos);
			return local.X >= grid.Min.X - 1 && local.X <= grid.Max.X + 1 &&
			       local.Y >= grid.Min.Y - 1 && local.Y <= grid.Max.Y + 1 &&
			       local.Z >= grid.Min.Z - 1 && local.Z <= grid.Max.Z + 1;
		}

		internal IMyCubeGrid GetCurrentEngineerGrid()
		{
			var ec = Utilities.GetEngineerCenter(character);
			var sphere = new BoundingSphereD(ec, 10);
			var entities = MyEntities.GetTopMostEntitiesInSphere(ref sphere);

			IMyCubeGrid result = null;
			double minimalDistanceSq = double.MaxValue;

			foreach (var e in entities)
			{
				var g = e as IMyCubeGrid;
				if (g == null || g.GridSizeEnum != MyCubeSize.Large) continue;

				if(!IsEngineerInsideGrid(g)) continue;

				double distanceSq = (ec - g.PositionComp.WorldAABB.Center).LengthSquared();
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

				ListFreeSpace_ToSb(ijk, sb);

				yield return sb.ToString();
			}

            up = selectedGrid.WorldMatrix.Up;

			Vector3D fromWorld = Utilities.GetEngineerCenter(character);
            
            var from = selectedGrid.WorldToGridInteger(fromWorld);
            var to = ijk;

			RunAstar(to, from); // ! Reversed

			for(;;)
			{	yield return NavigationStep();				
			}
		}

        public void RunAstar(Vector3I point_A, Vector3I point_B)
		{
            var grid = selectedGrid;

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

        internal string CharacterCellText()
		{	Vector3D e = Utilities.GetEngineerCenter(character);
			return IJK(selectedGrid.WorldToGridInteger(e));
		}

        internal string NavigationStep()
		{
            var grid = selectedGrid;

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
						var v = ar[ar.Count - i - 1] + grid.Min - AStarBorder; // ! Reverse back

						path.Add(grid.GridIntegerToWorld(v));				
					}

					MyConsole.Add($"path.Count {path.Count}", Color.IndianRed);

                    var jetComp = character.Components.Get<MyCharacterJetpackComponent>();
                    jetComp.TurnOnJetpack(true);

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

        public void CharacterRotateTo(Vector3D target)
		{
			var cm = character.GetHeadMatrix(true);
			var center = cm.Translation;

			Vector2 rotation;
			float roll;
			springController.Update(center, cm.Forward, cm.Up,
				target, cm.Up, 0.2, out rotation, out roll);

			character.MoveAndRotate(Vector3.Zero, rotation, roll);
		}

        internal void DrawPath()
		{	if(astar == null) return;

            var grid = selectedGrid;

			foreach(var p in astar.result)
			{	var iv = p + grid.Min - AStarBorder;
				Drawing.RoundMarker(grid.GridIntegerToWorld(iv), Color.DarkMagenta);
			}
		}
    }
}
