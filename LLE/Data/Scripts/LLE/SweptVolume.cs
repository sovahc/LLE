using System.Collections.Generic;

using VRageMath;
using VRage.Game.ModAPI;

namespace LLE
{
	/// <summary>
	/// Swept-volume collision check along a line segment using DDA cell traversal.
	/// Visits O(path_length) cells instead of O(AABB_volume).
	/// </summary>
	static class SweptVolume
	{
		static readonly List<Vector3I> _ddaCells = new List<Vector3I>();
		static readonly HashSet<Vector3I> _expandedCells = new HashSet<Vector3I>();
		static readonly List<Vector3> _modelVerts = new List<Vector3>();

		/// <summary>
		/// Returns true if worldConvex intersects any block geometry along the line.
		/// DDA enumerates cells on the centerline; ±1 expand covers the capsule radius.
		/// </summary>
		internal static bool ConvexVsGridAlongLine(IMyCubeGrid grid, List<Vector3D> worldConvex,
			Vector3D lineFrom, Vector3D lineTo)
		{
			MatrixD invWorld = grid.PositionComp.WorldMatrixNormalizedInv;
			Vector3D halfOffset = new Vector3D(grid.GridSize * 0.5f);
			Vector3D localFrom = Vector3D.Transform(lineFrom, invWorld) + halfOffset;
			Vector3D localTo = Vector3D.Transform(lineTo, invWorld) + halfOffset;

			_ddaCells.Clear();
			GridIntersection.Calculate(_ddaCells, grid.GridSize, localFrom, localTo, grid.Min, grid.Max);

			_expandedCells.Clear();
			foreach (var cell in _ddaCells)
				for (int dx = -1; dx <= 1; dx++)
					for (int dy = -1; dy <= 1; dy++)
						for (int dz = -1; dz <= 1; dz++)
							_expandedCells.Add(new Vector3I(cell.X + dx, cell.Y + dy, cell.Z + dz));

			foreach (var cell in _expandedCells)
			{
				var slim = grid.GetCubeBlock(cell);
				if (slim == null) continue;

				CollisionGeometry geometry;
				if (!Collisions._collisionGeometry.TryGetValue(slim.BlockDefinition.Id, out geometry))
					return true; // unknown block → treat as solid

				var w2m = Transform.GetWorldToModelMatrix(slim);
				_modelVerts.Clear();
				for (int i = 0; i < worldConvex.Count; i++)
					_modelVerts.Add(Vector3D.Transform(worldConvex[i], w2m));

				if (Collisions.ConvexVsBlockGeometry(geometry, _modelVerts))
					return true;
			}
			return false;
		}
	}
}
