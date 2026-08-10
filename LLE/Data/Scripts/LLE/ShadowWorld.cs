using System;
using System.Collections;
using System.Collections.Generic;

using VRage;
using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Definitions;
using Sandbox.ModAPI;
using MyInventoryItem = VRage.Game.ModAPI.Ingame.MyInventoryItem;
using IMyInventory = VRage.Game.ModAPI.Ingame.IMyInventory;

namespace LLE
{
	// An overlay on top of a real world. Reads fall through until something is written; writes stay
	// here. One instance serves one batch of commands, and throwing it away throws the prediction
	// away with it.
	//
	// Unknown is the third answer beside yes and no. The shadow gives it whenever only the engine
	// could answer — the identity of a block it placed itself, grid topology after a removal,
	// anything reached through the whole IMyCubeGrid. Validation of the batch stops at that command
	// and the batch keeps the prefix it earned; Unknown never rejects.
	public class ShadowWorld : IWorld
	{
		private const int MaxSteps = 20000;

		private readonly IWorld real;
		private readonly Dictionary<IMyCubeGrid, ShadowGrid> views = new Dictionary<IMyCubeGrid, ShadowGrid>();

		private readonly Dictionary<IMySlimBlock, float> integrity = new Dictionary<IMySlimBlock, float>();
		private readonly HashSet<IMySlimBlock> razed = new HashSet<IMySlimBlock>();

		private readonly Dictionary<IMyInventory, Dictionary<MyDefinitionId, MyFixedPoint>> amounts =
			new Dictionary<IMyInventory, Dictionary<MyDefinitionId, MyFixedPoint>>();
		private readonly HashSet<IMyInventory> reindexed = new HashSet<IMyInventory>();

		private Vector3D? engineer;
		private string tool;
		private int steps;

		public string Unknown { get; private set; }

		public ShadowWorld(IWorld real_)
		{
			real = real_;
		}

		internal void Unsure(string reason)
		{
			if (Unknown == null) Unknown = reason;
		}

		// Called once per coroutine step by whoever drives the batch. A loop whose exit condition
		// the shadow cannot reproduce would otherwise spin the game forever.
		public bool Step()
		{
			if (++steps > MaxSteps) Unsure("the command did not finish within the shadow iteration cap");
			return Unknown == null;
		}

		public IGridView View(IMyCubeGrid grid)
		{
			if (grid == null) return null;

			ShadowGrid view;
			if (!views.TryGetValue(grid, out view))
			{	view = new ShadowGrid(this, real.View(grid));
				views.Add(grid, view);
			}
			return view;
		}

		public Vector3D EngineerCenter => engineer ?? real.EngineerCenter;

		public MatrixD EngineerMatrix
		{
			get
			{	if (engineer.HasValue) Unsure("which way the engineer faces after a predicted move");
				return real.EngineerMatrix;
			}
		}

		public float Integrity(IMySlimBlock block)
		{
			if (razed.Contains(block)) return 0;

			float value;
			return integrity.TryGetValue(block, out value) ? value : real.Integrity(block);
		}

		public bool IsDestroyed(IMySlimBlock block)
		{
			return razed.Contains(block) || Integrity(block) == 0 && real.IsDestroyed(block);
		}

		public bool StockpileEmpty(IMySlimBlock block)
		{
			return razed.Contains(block) || real.StockpileEmpty(block);
		}

		public bool CanContinueBuild(IMySlimBlock block, IMyInventory inventory) => real.CanContinueBuild(block, inventory);

		// The welding effect belongs to ToolShoot; here the components would leave the engineer's
		// real inventory for a block that is only being predicted.
		public void MoveItemsToConstructionStockpile(IMySlimBlock block, IMyInventory inventory) { }

		public void GetItems(IMyInventory inventory, List<MyInventoryItem> items)
		{
			if (amounts.ContainsKey(inventory)) Unsure("the contents of an inventory the batch has already moved items through");
			real.GetItems(inventory, items);
		}

		public MyFixedPoint ItemAmount(IMyInventory inventory, MyDefinitionId item)
		{
			Dictionary<MyDefinitionId, MyFixedPoint> byId;
			MyFixedPoint amount;
			if (amounts.TryGetValue(inventory, out byId) && byId.TryGetValue(item, out amount)) return amount;
			return real.ItemAmount(inventory, item);
		}

		// Deliberately not tracked: an over-estimate lets a doomed batch through, an under-estimate
		// rejects a good one, and only the second mistake is expensive.
		public MyFixedPoint AmountThatFits(IMyInventory inventory, MyDefinitionId item) => real.AmountThatFits(inventory, item);

		public void SetPause(double time) { }
		public bool IsPaused() => false;
		public bool ToolEquipped => tool != null;

		public bool ToolReady(out MyGunStatusEnum status)
		{
			status = MyGunStatusEnum.OK;
			return true;
		}

		// Reachability is the path finder's answer, and the path finder reads the real grid. Letting
		// the flight through keeps the batch alive for the commands that follow it, which is where
		// the prediction is worth something.
		public IEnumerator FlyTo(IGridView grid, Vector3I destinationCell, string arrivalMessage, bool headFirst)
		{
			engineer = grid.GridIntegerToWorld(destinationCell);
			yield return CommandResult.Success(string.IsNullOrEmpty(arrivalMessage)
				? $"Arrived. Position: {destinationCell}" : arrivalMessage);
		}

		public void Move(Vector3D target, double desiredSpeed = 5.0)
		{
			engineer = target + Constants.EngineerHeight/2 * real.EngineerMatrix.Up;
		}

		public void RotateTo(Vector3D target) { }
		public void SwitchCubePlacer(bool hold) { }

		public bool EquipTool(string subtype)
		{
			tool = subtype;
			return true;
		}

		public void ToolShoot(IMySlimBlock target)
		{
			if (target == null) { Unsure("what the tool is pointed at"); return; }
			if (tool == null) { Unsure("which tool is in hand"); return; }

			if (tool.IndexOf("Welder", StringComparison.OrdinalIgnoreCase) >= 0)
			{	integrity[target] = target.MaxIntegrity;
				return;
			}

			if (tool.IndexOf("Grinder", StringComparison.OrdinalIgnoreCase) >= 0)
			{	integrity[target] = 0;
				razed.Add(target);
				return;
			}

			Unsure($"what the {tool} does to a block");
		}

		public void ToolStop() { }

		// Being seated moves the engineer, changes which grid he is on and hands control to the
		// cockpit — none of which the shadow models, and all of which would be real if it let the
		// call through.
		public bool AttachPilot(IMyCockpit cockpit)
		{
			Unsure("what happens once the engineer is seated");
			return true;
		}

		public void RemovePilot(IMyCockpit cockpit) => Unsure("where the engineer ends up after leaving a seat");

		public bool PlaceBlock(IGridView grid, MyObjectBuilder_CubeBlock ob)
		{
			var shadow = grid as ShadowGrid;
			if (shadow == null) { Unsure("a grid that is not part of the prediction"); return false; }

			var definition = MyDefinitionManager.Static.GetCubeBlockDefinition(ob);
			if (definition == null) { Unsure($"the block type {ob.GetId()}"); return false; }

			shadow.Add(ob.Min, definition, ob.BlockOrientation);
			return true;
		}

		public void RazeBlock(IMySlimBlock block)
		{
			razed.Add(block);

			var shadow = View(block.CubeGrid) as ShadowGrid;
			if (shadow == null) { Unsure("a grid that is not part of the prediction"); return; }

			shadow.Remove(block.Min, block.Max);
		}

		public bool TransferItemTo(IMyInventory from, int fromIndex, IMyInventory to, MyFixedPoint amount, bool requireConveyor)
		{
			// Slots are addressed by index, and the shadow keeps amounts rather than a slot list —
			// so the first transfer out of an inventory is predictable and the next one is not.
			if (reindexed.Contains(from)) { Unsure("which item now sits in that inventory slot"); return true; }
			reindexed.Add(from);

			var items = new List<MyInventoryItem>();
			real.GetItems(from, items);
			if (fromIndex < 0 || fromIndex >= items.Count) return false;

			MyDefinitionId item = items[fromIndex].Type;
			if (amount == 0) amount = items[fromIndex].Amount;

			Set(from, item, ItemAmount(from, item) - amount);
			Set(to, item, ItemAmount(to, item) + amount);
			return true;
		}

		private void Set(IMyInventory inventory, MyDefinitionId item, MyFixedPoint amount)
		{
			Dictionary<MyDefinitionId, MyFixedPoint> byId;
			if (!amounts.TryGetValue(inventory, out byId))
			{	byId = new Dictionary<MyDefinitionId, MyFixedPoint>();
				amounts.Add(inventory, byId);
			}
			byId[item] = amount;
		}
	}

	// Cells the batch has changed, over the real grid underneath. A block the shadow placed exists
	// as a cell and a definition and nothing more: there is no IMySlimBlock to hand out, so every
	// question that wants the object itself becomes Unknown.
	public class ShadowGrid : IGridView
	{
		private readonly ShadowWorld world;
		private readonly IGridView real;

		private readonly Dictionary<Vector3I, MyCubeBlockDefinition> added = new Dictionary<Vector3I, MyCubeBlockDefinition>();
		private readonly Dictionary<Vector3I, MyBlockOrientation> orientations = new Dictionary<Vector3I, MyBlockOrientation>();
		private readonly HashSet<Vector3I> removed = new HashSet<Vector3I>();

		public ShadowGrid(ShadowWorld world_, IGridView real_)
		{
			world = world_;
			real = real_;
		}

		private bool Mutated => added.Count != 0 || removed.Count != 0;

		internal void Add(Vector3I cell, MyCubeBlockDefinition definition, MyBlockOrientation orientation)
		{
			removed.Remove(cell);
			added[cell] = definition;
			orientations[cell] = orientation;
		}

		internal void Remove(Vector3I min, Vector3I max)
		{
			for (int x = min.X; x <= max.X; ++x)
			for (int y = min.Y; y <= max.Y; ++y)
			for (int z = min.Z; z <= max.Z; ++z)
			{	var cell = new Vector3I(x, y, z);
				added.Remove(cell);
				orientations.Remove(cell);
				removed.Add(cell);
			}
		}

		public MyCubeBlockDefinition CellDefinition(Vector3I pos)
		{
			MyCubeBlockDefinition definition;
			if (added.TryGetValue(pos, out definition)) return definition;
			if (removed.Contains(pos)) return null;
			return real.CellDefinition(pos);
		}

		public IMySlimBlock GetCubeBlock(Vector3I pos)
		{
			if (added.ContainsKey(pos))
			{	world.Unsure($"a block the batch placed at {pos} — the engine has not made it yet");
				return null;
			}
			if (removed.Contains(pos)) return null;
			return real.GetCubeBlock(pos);
		}

		public void GetBlocks(List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			if (Mutated) world.Unsure("the full block list of a grid the batch has changed");
			real.GetBlocks(blocks, collect);
		}

		public Vector3D GridIntegerToWorld(Vector3I gridCoords) => real.GridIntegerToWorld(gridCoords);
		public Vector3I WorldToGridInteger(Vector3D coords) => real.WorldToGridInteger(coords);
		public MyCubeSize GridSizeEnum => real.GridSizeEnum;
		public float GridSize => real.GridSize;
		public MatrixD WorldMatrix => real.WorldMatrix;

		public IMyCubeGrid Grid
		{
			get
			{	if (Mutated) world.Unsure("anything read off the grid itself once the batch has changed it");
				return real.Grid;
			}
		}
	}
}
