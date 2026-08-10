using System;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;

namespace LLE
{
	// Members mirror IMyCubeGrid exactly so that call sites compile unchanged when
	// selectedGrid switches to this type. RealGrid passes through to the game,
	// ShadowWorld overlays predicted changes on top of it.
	public interface IGridView
	{
		IMySlimBlock GetCubeBlock(Vector3I pos);
		void GetBlocks(List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null);
		Vector3D GridIntegerToWorld(Vector3I gridCoords);
		Vector3I WorldToGridInteger(Vector3D coords);
		MyCubeSize GridSizeEnum { get; }
		float GridSize { get; }
		MatrixD WorldMatrix { get; }

		// EQS, AStarHelper, ConveyorAStar and Draft take the grid itself and are not routed
		// through the view; in the shadow they therefore see the unmodified world. Any command
		// reaching them after a shadow mutation must report Unknown.
		IMyCubeGrid Grid { get; }
	}

	public class RealGrid : IGridView
	{
		private readonly IMyCubeGrid grid;

		public RealGrid(IMyCubeGrid grid_)
		{
			grid = grid_;
		}

		public IMySlimBlock GetCubeBlock(Vector3I pos) => grid.GetCubeBlock(pos);
		public void GetBlocks(List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null) => grid.GetBlocks(blocks, collect);
		public Vector3D GridIntegerToWorld(Vector3I gridCoords) => grid.GridIntegerToWorld(gridCoords);
		public Vector3I WorldToGridInteger(Vector3D coords) => grid.WorldToGridInteger(coords);
		public MyCubeSize GridSizeEnum => grid.GridSizeEnum;
		public float GridSize => grid.GridSize;
		public MatrixD WorldMatrix => grid.WorldMatrix;
		public IMyCubeGrid Grid => grid;
	}
}
