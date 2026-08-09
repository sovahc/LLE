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
		private readonly int ResolutionBits;
		private readonly List<Vector3D> sweptCapsule = new List<Vector3D>();

		internal List<Vector3I> SmoothPath(List<Vector3I> path)
		{
			path = RemoveCollinear(path);

			if (path.Count <= 2) return path;

			//return path;

			var result = new List<Vector3I> { path[0] };
			int i = 0;
			while (i < path.Count - 1)
			{
				int j = path.Count - 1;
				while (j > i + 1 && !LineIsClear(path[i], path[j]))
					j--;
				result.Add(path[j]);
				i = j;
			}
			return result;
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

		internal List<Vector3I> RemoveCollinear(List<Vector3I> path)
		{
			if (path.Count <= 2) return path;

			int maxStep = 1000 << ResolutionBits;
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

		internal Vector3D CellToWorld(Vector3I cell)
		{
			var sub = cell & ((1 << ResolutionBits) - 1);
			var localOffset = new Vector3D(sub) * (grid.GridSize / (double)(1 << ResolutionBits));
			return grid.GridIntegerToWorld(CellToBlock(cell)) + Vector3D.TransformNormal(localOffset, grid.WorldMatrix);
		}

		internal Vector3I CellToBlock(Vector3I cell)
		{
			return cell >> ResolutionBits;
		}

		internal AStarHelper(IMyCubeGrid grid_, Vector3I point_A, Vector3I point_B)
		{	grid = grid_;

			if(grid.GridSizeEnum == MyCubeSize.Large) AStarBorder = 2;
			if(grid.GridSizeEnum == MyCubeSize.Small) AStarBorder = 7;

			MyConsole.Add($"RunAstar '{grid.DisplayName}' {grid.Min} {grid.Max} {point_A} -> {point_B}");

			ResolutionBits = grid.GridSizeEnum == MyCubeSize.Large ? 1 : 0;

			var boxMin = (grid.Min - AStarBorder) << ResolutionBits;
			var boxMax = ((grid.Max + AStarBorder) << ResolutionBits) + ((1 << ResolutionBits) - 1);

			var source = new TraversabilityCalculator(grid, ResolutionBits);

			astar = new AStar(boxMin, boxMax, source);

			astar.RunCalculation(point_A << ResolutionBits, point_B << ResolutionBits);
		}

		internal bool Tick()
		{	
			if (astar.Completed()) return true;

			astar.Iteration();

			return astar.Completed();
		}
	}
}
