using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace LLE
{
	class TraversabilityCalculator
	{
		private readonly IMyCubeGrid _grid;
		private readonly int _border;

		public TraversabilityCalculator(IMyCubeGrid grid, int border)
		{
			_grid = grid;
			_border = border;
		}

		public Traversability Get(Vector3I position)
		{
			var gridPos = position + _grid.Min - _border;

			var slim = _grid.GetCubeBlock(gridPos);
			if (slim == null)
				return Traversability.Free;

			Traversability t;
			if (!Collisions._traversabilityCache.TryGetValue(slim.BlockDefinition.Id, out t))
				return Traversability.Blocked;

			if (slim.Min == slim.Max)
			{
				MatrixI m = new MatrixI(slim.Orientation);
				return Traversability.Rotate(t, m);
			}
			return Traversability.Blocked;
		}
	}
}
