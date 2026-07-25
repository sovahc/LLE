using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;

namespace LLE
{
    class AStarHelper
	{
		private readonly IMyCubeGrid grid;
		private readonly AStar astar;
		private readonly int AStarBorder;
		private readonly int Resolution;
		private readonly List<Vector3D> sweptCapsule = new List<Vector3D>();

		internal List<Vector3I> SmoothPath(List<Vector3I> path)
		{
			path = RemoveCollinear(path);

			if (path.Count <= 2) return path;

			return path;

			/*var result = new List<Vector3I> { path[0] };
			int i = 0;
			while (i < path.Count - 1)
			{
				int j = path.Count - 1;
				while (j > i + 1 && !LineIsClear(path[i], path[j]))
					j--;
				result.Add(path[j]);
				i = j;
			}
			return result;*/
		}

		private bool LineIsClear(Vector3I a, Vector3I b)
		{
			var from = CellToWorld(a);
			var to = CellToWorld(b);

			var dir = to - from;
			if (dir.LengthSquared() < 0.01) return true;

			var mid = (from + to) * 0.5;
			var world = MatrixD.CreateWorld(mid, dir, grid.WorldMatrix.Up);

			sweptCapsule.Clear();
			Geometry.SweptCapsule(
				Constants.EngineerCapsuleHeight * 0.5,
				Constants.EngineerCapsuleRadius,
				dir.Length() * 0.5,
				sweptCapsule);

			for (int i = 0; i < sweptCapsule.Count; i++)
				sweptCapsule[i] = Vector3D.Transform(sweptCapsule[i], world);

			return !SweptVolume.ConvexVsGridAlongLine(grid, sweptCapsule, from, to);
		}

		// Remove collinear points: keep only where direction changes.
		internal List<Vector3I> RemoveCollinear(List<Vector3I> path)
		{
			if (path.Count <= 2) return path;

			int maxStep = 1000 * Resolution;
			var result = new List<Vector3I>(path.Count) { path[0] };
			int step = 0;

			for (int i = 1; i < path.Count - 1; i++)
			{
				step++;
				var prev = path[i] - path[i - 1];
				var next = path[i + 1] - path[i];
				if (prev != next || step >= maxStep)
				{
					result.Add(path[i]);
					step = 0;
				}
			}

			result.Add(path[path.Count - 1]);
			return result;
		}

		internal List<Vector3I> GetPath()
		{
			var result = new List<Vector3I>(astar.result.Count);

			for(int i = 0; i < astar.result.Count; ++i)
			{
				result.Add(astar.result[i]);
			}

			return result;
		}

		internal Vector3D CellToWorld(Vector3I coord)
		{
			var blockPos = coord / Resolution + grid.Min - AStarBorder;
			var sub = new Vector3I(coord.X % Resolution, coord.Y % Resolution, coord.Z % Resolution);
			var localOffset = new Vector3D(sub) * (grid.GridSize / (double)Resolution);
			return grid.GridIntegerToWorld(blockPos) + Vector3D.TransformNormal(localOffset, grid.WorldMatrix);
		}

		internal Vector3I CellToBlock(Vector3I coord)
		{
			return coord / Resolution + grid.Min - AStarBorder;
		}

		internal AStarHelper(IMyCubeGrid grid_, Vector3I point_A, Vector3I point_B)
		{	grid = grid_;

			if(grid.GridSizeEnum == MyCubeSize.Large) AStarBorder = 2;
			if(grid.GridSizeEnum == MyCubeSize.Small) AStarBorder = 7;

			Vector3I gridSize = grid.Max - grid.Min + 1;

			MyConsole.Add($"RunAstar '{grid.DisplayName}' {grid.Min} {grid.Max} ({gridSize}) {point_A} -> {point_B}");

			Resolution = grid.GridSizeEnum == MyCubeSize.Large ? 2 : 1;

			var astarSize = (gridSize + AStarBorder + AStarBorder) * Resolution;

			var source = new TraversabilityCalculator(grid, AStarBorder, Resolution);

			astar = new AStar(astarSize, source);

			astar.Reset();

			var a = (point_A - grid.Min + AStarBorder) * Resolution;
			var b = (point_B - grid.Min + AStarBorder) * Resolution;
			astar.RunCalculation(a, b);
		}

		internal bool Tick()
		{	
			if (astar.Completed()) return true;

			astar.Iteration();

			return astar.Completed();
		}
	}
}
