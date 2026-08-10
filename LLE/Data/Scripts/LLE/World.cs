using System;
using System.Collections;
using System.Collections.Generic;

using VRage;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.ModAPI;
using Sandbox.Definitions;
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
		// What stands in a cell, as opposed to which object stands there. A block the shadow has
		// placed answers the first question and not the second, so occupancy and adjacency tests
		// go through here.
		MyCubeBlockDefinition CellDefinition(Vector3I pos);

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

		public MyCubeBlockDefinition CellDefinition(Vector3I pos)
		{
			var block = grid.GetCubeBlock(pos);
			return block == null ? null : block.BlockDefinition as MyCubeBlockDefinition;
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
		IGridView View(IMyCubeGrid grid);

		Vector3D EngineerCenter { get; }
		MatrixD EngineerMatrix { get; }

		float Integrity(IMySlimBlock block);
		bool IsDestroyed(IMySlimBlock block);
		bool StockpileEmpty(IMySlimBlock block);
		bool CanContinueBuild(IMySlimBlock block, IMyInventory inventory);

		// Moves components out of the engineer's inventory for real, so the shadow must not let
		// this one through.
		void MoveItemsToConstructionStockpile(IMySlimBlock block, IMyInventory inventory);

		void GetItems(IMyInventory inventory, List<MyInventoryItem> items);
		MyFixedPoint ItemAmount(IMyInventory inventory, MyDefinitionId item);
		MyFixedPoint AmountThatFits(IMyInventory inventory, MyDefinitionId item);

		// Pacing. Commands wait out MicronavigationDelay and tool cooldown in `while` loops;
		// in the shadow both are over at once, or every loop burns the iteration cap.
		void SetPause(double time);
		bool IsPaused();
		bool ToolEquipped { get; }
		bool ToolReady(out MyGunStatusEnum status);

		// A whole flight is one effect. R runs the path search and the per-tick control loop; the
		// shadow has neither, and arrives on the spot.
		IEnumerator FlyTo(IGridView grid, Vector3I destinationCell, string arrivalMessage, bool headFirst);

		void Move(Vector3D target, double desiredSpeed = 5.0);
		void RotateTo(Vector3D target);
		void SwitchCubePlacer(bool hold);

		// The shadow has no equipped tool of its own; this call is how it learns whether the next
		// ToolShoot welds or grinds.
		bool EquipTool(string subtype);

		// Native grinding and welding raycast their own target, so the real world ignores
		// `target`; the shadow has no raycast and needs to be told what is being worked on.
		void ToolShoot(IMySlimBlock target);
		void ToolStop();

		bool AttachPilot(IMyCockpit cockpit);
		void RemovePilot(IMyCockpit cockpit);

		bool PlaceBlock(IGridView grid, MyObjectBuilder_CubeBlock ob);
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

		public IGridView View(IMyCubeGrid grid) => grid == null ? null : new RealGrid(grid);

		public Vector3D EngineerCenter => character.GetPosition() + Constants.EngineerHeight/2 * character.WorldMatrix.Up;
		public MatrixD EngineerMatrix => character.WorldMatrix;

		public float Integrity(IMySlimBlock block) => block.Integrity;
		public bool IsDestroyed(IMySlimBlock block) => block.IsDestroyed;
		public bool StockpileEmpty(IMySlimBlock block) => block.StockpileEmpty;
		public bool CanContinueBuild(IMySlimBlock block, IMyInventory inventory) => block.CanContinueBuild((WTF_IMyInventory)inventory);
		public void MoveItemsToConstructionStockpile(IMySlimBlock block, IMyInventory inventory) => block.MoveItemsToConstructionStockpile((WTF_IMyInventory)inventory);

		public void GetItems(IMyInventory inventory, List<MyInventoryItem> items) => inventory.GetItems(items);
		public MyFixedPoint ItemAmount(IMyInventory inventory, MyDefinitionId item) => ((WTF_IMyInventory)inventory).GetItemAmount(item);
		public MyFixedPoint AmountThatFits(IMyInventory inventory, MyDefinitionId item)
		{
			var inv = inventory as MyInventory;
			return inv == null ? 0 : inv.ComputeAmountThatFits(item);
		}

		public void SetPause(double time) => commands.SetPause(time);
		public bool IsPaused() => commands.IsPaused();

		public bool ToolEquipped => character.EquippedTool is IMyGunObject<MyDeviceBase>;

		public bool ToolReady(out MyGunStatusEnum status)
		{
			status = MyGunStatusEnum.Failed;
			var gun = character.EquippedTool as IMyGunObject<MyDeviceBase>;
			return gun != null && gun.CanShoot(MyShootActionEnum.PrimaryAction, character.EntityId, out status);
		}

		public IEnumerator FlyTo(IGridView grid, Vector3I destinationCell, string arrivalMessage, bool headFirst)
			=> commands.RealFly(destinationCell, arrivalMessage, headFirst);

		public void Move(Vector3D target, double desiredSpeed = 5.0) => commands.CharacterMove(target, desiredSpeed);
		public void RotateTo(Vector3D target) => commands.CharacterRotateTo(target);
		public void SwitchCubePlacer(bool hold) => commands.SwitchCubePlacer(hold);
		public bool EquipTool(string subtype) => commands.EquipTool(subtype);

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

		// AttachPilot silently no-ops if it fails; verify by re-checking Pilot.
		public bool AttachPilot(IMyCockpit cockpit)
		{
			cockpit.AttachPilot(character, 0);
			return cockpit.Pilot != null && cockpit.Pilot.EntityId == character.EntityId;
		}

		public void RemovePilot(IMyCockpit cockpit) => cockpit.RemovePilot();

		public bool PlaceBlock(IGridView grid, MyObjectBuilder_CubeBlock ob) => grid.Grid.AddBlock(ob, false) != null;

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
