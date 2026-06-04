using System.Collections.Generic;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace LLE
{
	class TraversabilitySource
	{
		private readonly IMyCubeGrid _grid;
		private readonly Vector3I _border;
		private readonly int _strideXY;
		private readonly int _sizeX;
		private readonly Dictionary<MyDefinitionId, Traversability> _cache;

		public TraversabilitySource(IMyCubeGrid grid, int border, Vector3I astarSize, 
									Dictionary<MyDefinitionId, Traversability> cache)
		{
			_grid = grid;
			_border = new Vector3I(border);
			_sizeX = astarSize.X;
			_strideXY = astarSize.X * astarSize.Y;
			_cache = cache;
		}

		public Traversability Get(int index)
		{
			Vector3I pos;
			int strideXY = _strideXY;
			pos.Z = index / strideXY;
			index -= pos.Z * strideXY;
			pos.Y = index / _sizeX;
			pos.X = index - pos.Y * _sizeX;

			var gridPos = pos - _border;

			if (gridPos.X < _grid.Min.X || gridPos.Y < _grid.Min.Y || gridPos.Z < _grid.Min.Z ||
				gridPos.X > _grid.Max.X || gridPos.Y > _grid.Max.Y || gridPos.Z > _grid.Max.Z)
				return Traversability.Free;

			var slim = _grid.GetCubeBlock(gridPos) as IMySlimBlock;
			if (slim == null)
				return Traversability.Free;

			Traversability t;
			if (!_cache.TryGetValue(slim.BlockDefinition.Id, out t))
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
		}
	}
}
