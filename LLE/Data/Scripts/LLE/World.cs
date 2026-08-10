using System;
using System.Collections.Generic;

using VRage;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Game;
using Sandbox.Game.Entities;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;
using WTF_IMyInventory = VRage.Game.ModAPI.IMyInventory;

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

	// Everything a command may read or change outside its own arguments. Blocks and
	// inventories travel through the commands as game handles, so their mutable state is
	// read from here instead of off the handle — otherwise the shadow cannot move it.
	//
	// Effects are semantic, not native: MoveAndRotate and Shoot cannot be answered by a
	// shadow without simulating physics, while "move to this point" and "advance this
	// block" can.
	public interface IWorld
	{
		Vector3D EngineerCenter { get; }
		MatrixD EngineerMatrix { get; }

		float Integrity(IMySlimBlock block);
		bool IsDestroyed(IMySlimBlock block);
		bool StockpileEmpty(IMySlimBlock block);

		void GetItems(IMyInventory inventory, List<MyInventoryItem> items);
		MyFixedPoint ItemAmount(IMyInventory inventory, MyDefinitionId item);
		MyFixedPoint AmountThatFits(IMyInventory inventory, MyDefinitionId item);

		// Pacing. Commands wait out MicronavigationDelay and tool cooldown in `while` loops;
		// in the shadow both are over at once, or every loop burns the iteration cap.
		void SetPause(double time);
		bool IsPaused();
		bool ToolReady(out MyGunStatusEnum status);

		void Move(Vector3D target, double desiredSpeed);
		void RotateTo(Vector3D target);
		void SwitchCubePlacer(bool hold);

		// Native grinding and welding raycast their own target, so the real world ignores
		// `target`; the shadow has no raycast and needs to be told what is being worked on.
		void ToolShoot(IMySlimBlock target);
		void ToolStop();

		IMySlimBlock PlaceBlock(IGridView grid, MyObjectBuilder_CubeBlock ob);
		void RazeBlock(IMySlimBlock block);
		bool TransferItemTo(IMyInventory from, int fromIndex, IMyInventory to, MyFixedPoint amount, bool requireConveyor);
	}

	// Thin adapter over what Commands already does. The navigation and tool code stays where
	// it works until the seam is proven in game; moving it in here is a later cleanup.
	public class RealWorld : IWorld
	{
		private readonly Commands commands;
		private readonly IMyCharacter character;

		public RealWorld(Commands commands_, IMyCharacter character_)
		{
			commands = commands_;
			character = character_;
		}

		public Vector3D EngineerCenter => commands.GetEngineerCenter();
		public MatrixD EngineerMatrix => character.WorldMatrix;

		public float Integrity(IMySlimBlock block) => block.Integrity;
		public bool IsDestroyed(IMySlimBlock block) => block.IsDestroyed;
		public bool StockpileEmpty(IMySlimBlock block) => block.StockpileEmpty;

		public void GetItems(IMyInventory inventory, List<MyInventoryItem> items) => inventory.GetItems(items);
		public MyFixedPoint ItemAmount(IMyInventory inventory, MyDefinitionId item) => ((WTF_IMyInventory)inventory).GetItemAmount(item);
		public MyFixedPoint AmountThatFits(IMyInventory inventory, MyDefinitionId item)
		{
			var inv = inventory as MyInventory;
			return inv == null ? 0 : inv.ComputeAmountThatFits(item);
		}

		public void SetPause(double time) => commands.SetPause(time);
		public bool IsPaused() => commands.IsPaused();

		public bool ToolReady(out MyGunStatusEnum status)
		{
			status = MyGunStatusEnum.Failed;
			var gun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
			return gun != null && gun.CanShoot(MyShootActionEnum.PrimaryAction, character.EntityId, out status);
		}

		public void Move(Vector3D target, double desiredSpeed) => commands.CharacterMove(target, desiredSpeed);
		public void RotateTo(Vector3D target) => commands.CharacterRotateTo(target);
		public void SwitchCubePlacer(bool hold) => commands.SwitchCubePlacer(hold);

		public void ToolShoot(IMySlimBlock target)
		{
			var gun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
			gun?.Shoot(MyShootActionEnum.PrimaryAction, (Vector3)character.WorldMatrix.Forward, null);
		}

		public void ToolStop()
		{
			var gun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
			gun?.EndShoot(MyShootActionEnum.PrimaryAction);
		}

		public IMySlimBlock PlaceBlock(IGridView grid, MyObjectBuilder_CubeBlock ob) => grid.Grid.AddBlock(ob, false);

		public void RazeBlock(IMySlimBlock block)
		{
			block.SpawnConstructionStockpile();
			block.CubeGrid.RazeBlock(block.Min);
		}

		public bool TransferItemTo(IMyInventory from, int fromIndex, IMyInventory to, MyFixedPoint amount, bool requireConveyor)
		{
			return ((WTF_IMyInventory)from).TransferItemTo((WTF_IMyInventory)to, fromIndex, null, true, amount, requireConveyor);
		}
	}
}
