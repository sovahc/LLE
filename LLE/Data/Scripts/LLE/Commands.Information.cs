using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using WTF_IMyInventory = VRage.Game.ModAPI.IMyInventory;
using IMyTerminalBlock = Sandbox.ModAPI.IMyTerminalBlock;
using Sandbox.Game;
using Sandbox.Game.EntityComponents;
using VRage;

namespace LLE
{
	public partial class Commands
	{	
		private readonly List<string> ALL_COMPONENTS = new List<string>();
		
		private static readonly List<IMyTerminalBlock> terminalBlocks = new List<IMyTerminalBlock>();
		private static readonly Dictionary<string, List<Vector3I>> describer = new Dictionary<string, List<Vector3I>>();
		private static readonly Dictionary<string, IMyTerminalBlock> nameToSample = new Dictionary<string, IMyTerminalBlock>();
		private static readonly List<Vector3I> positions = new List<Vector3I>();

		private static string Categorize(IMyTerminalBlock block)
		{
			var def = block.SlimBlock.BlockDefinition;

			// IMyShipController (cockpit/remote control/cryopod)

			// IMyBasicMissionBlock IMyStoreBlock IMyVendingMachine
			// IMyMechanicalConnectionBlock
			// IMySmallGatlingGun IMySmallMissileLauncher IMySmallMissileLauncherReload
			// IMySolarFoodGenerator
			// IMyTargetDummyBlock

			if (IsSurvivalKit(block))
				return "Life Support & Production";
			if (block is IMyProductionBlock || // IMyAssembler IMyRefinery
				block is IMyUpgradeModule)
				return "Production";
			if (block is IMyMedicalRoom) // IMyGasBlock
				return "Life Support";

			if (block is IMyCryoChamber) // IMyCryoChamber is IMyCockpit
				return "Rest & Sleep";

			var cockpit = block as IMyCockpit;
			if (cockpit != null)
			{	var cDef = (MyCockpitDefinition)def;
				//bool hasOxygen = cockpit.OxygenCapacity > 0;
				//if(hasOxygen) return "Enclosed Cockpit";
				return cDef.EnableShipControl ? "Cockpit" : "Seats";
			}

			if (block is IMyRemoteControl)
				return "Remote Control";
			if (block is IMyPowerProducer || block is IMySolarPanel) // IMyBatteryBlock, IMyReactor, IMyWindTurbine
				return "Energy";
			if (block is IMyLargeTurretBase || block is IMyDecoy)
				return "Defense";
			if (block is IMyWarhead)
				return "Explosives";
			if (block is IMyShipGrinder || block is IMyShipWelder || block is IMyProjector)
				return "Construction";
			if (block is IMyShipDrill || block is IMyOreDetector)
				return "Mining";
			if (block is IMyBeacon || block is IMyRadioAntenna || block is IMyLaserAntenna)
				return "Communication";
			if (block is IMyShipConnector || block is IMyCollector || block is IMyLandingGear)
				return "Docking";
			if (block is IMyGasTank || block is IMyGasGenerator || block is IMyAirVent || block is IMyOxygenFarm)
				return "Gas";
			if (block is IMyButtonPanel)
				return "Buttons";
			if (block is IMyTimerBlock)
				return "Timers";
			if (block is IMySensorBlock)
				return "Sensors";
			if (block is IMyProgrammableBlock)
				return "Programmable Blocks";
			if (block is IMyBroadcastController ||
				block is IMyEventControllerBlock || block is IMyEventComponentWithGui ||
				block is IMyEmotionControllerBlock ||
				block is IMyFlightMovementBlock ||
				block is IMyOffensiveCombatBlock || block is IMyDefensiveCombatBlock ||
				block is IMyTurretControlBlock ||
				block is IMyPathRecorderBlock)
				return "Computers";
			if (block is IMyAirtightHangarDoor)
				return "Hangar Doors";
			if (block is IMyDoor)
				return "Doors";
			if (block is IMyGravityGeneratorBase || block is IMyVirtualMass || block is IMySpaceBall)
				return "Gravity";
			if (block is IMyMotorStator || block is IMyMotorRotor) // IMyMotorBase?
				return "Rotors";
			if (block is IMyPistonBase || block is IMyPistonTop)
				return "Pistons";
			if(block is IMyWheel || block is IMyMotorSuspension)
				return "Wheels";
			if (block is IMyThrust || block is IMyGyro || block is IMyJumpDrive)
				return "Movement";
			if (block is IMyCargoContainer)
				return "Storage";
			if (block is IMyConveyorSorter) // (IMyConveyor, IMyConveyorTube) -> IMyCubeBlock
				return "Conveyor";
			if (block is IMyCameraBlock)
				return "Cameras";
			if (block is IMyLightingBlock || block is IMySearchlight)
				return "Lights";
			if (block is IMyTextPanel)
				return "Displays";
			if (block is IMyExhaustBlock || block is IMyHeatVent || block is IMySoundBlock)
				return "Decoration";
			return "Other";
		}

		internal void ListDescription(List<Vector3I> coordinates, bool byCategory, MyMarkdown md)
		{	md.Append($"Legend: Name → count (positions on the grid)");

			describer.Clear();
			nameToSample.Clear();

			foreach (var position in coordinates)
			{	
				var cubeBlock = selectedGrid.GetCubeBlock(position);
				var name = Name(cubeBlock);

				List<Vector3I> pp;
				if(!describer.TryGetValue(name, out pp))
				{	pp = new List<Vector3I>();
					describer[name] = pp;
					if (cubeBlock != null && cubeBlock.FatBlock != null)
						nameToSample[name] = cubeBlock.FatBlock as IMyTerminalBlock;
				}
				pp.Add(position);
			}

			foreach (var kv in describer)
			{	
				var name = kv.Key;
				IMyTerminalBlock sample;
				var category = byCategory && nameToSample.TryGetValue(name, out sample) ? Categorize(sample) : null;

				StringBuilder sb = new StringBuilder();
				sb.Append($"* {Quote(kv.Key)} → {kv.Value.Count} (");

				bool semi = false;
				foreach(var p in kv.Value)
				{	if(semi) sb.Append("; ");
					sb.Append(IJK(p));
					semi = true;
				}
				sb.Append(")");

				if(byCategory)
					md.Add($"## {category}", sb.ToString());
				else
					md.Append(sb.ToString());
			}

			describer.Clear();
			nameToSample.Clear();
		}

		internal CommandResult Overview()
		{
			string message;
			if(!GridIsSet(out message)) return message;

			string category, name;
			Description(selectedGrid as MyEntity, out category, out name);

			var md = new MyMarkdown();
			md.Append($"# {category} '{name}'");
			md.Append($"## Information");

			md.Append($"Powered: {GridHasPower(selectedGrid)}");
			var cg = selectedGrid as MyCubeGrid;

			Vector3D size = cg.Max - cg.Min;
			size *= cg.GridSize;
			md.Append($"Linear size: {size}");
			md.Append($"Coordinate range: Min {IJK(cg.Min)} Max {IJK(cg.Max)}");
			md.Append($"Blocks count: {cg.CubeBlocks.Count}");
			md.Append($"Natural gravity: {cg.NaturalGravity.Length():F1}");

			if(!cg.IsStatic)
			{	md.Append($"Mass: {cg.Mass} kg");
				md.Append($"Dampeners enabled {cg.DampenersEnabled}");

				md.Append($"Linear velocity: {cg.LinearVelocity.Length():F1}");

				if(cg.Physics != null)
					md.Append($"Angular velocity: {cg.Physics.AngularVelocity.Length():F1}");
			}

			var group = new List<IMyCubeGrid>();
			MyAPIGateway.GridGroups.GetGroup(selectedGrid, GridLinkTypeEnum.Physical, group);
			if(group.Count > 1)
			{	md.Append($"## Physically connected to:");
				
				foreach(var g in group)
				{	if(g.EntityId == cg.EntityId) continue;
					md.Append($"* {Quote(Name(g))}");
				}
			}

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(selectedGrid);
			terminalBlocks.Clear();
			ts.GetBlocks(terminalBlocks);

			positions.Clear();
			foreach(var block in terminalBlocks)
			{	if(block.CubeGrid != selectedGrid) continue;
				positions.Add(block.SlimBlock.Position);
			}

			ListDescription(positions, true, md);

			terminalBlocks.Clear();
			positions.Clear();

			return Success(md.Result());
		}

		internal CommandResult Integrity()
		{
			string message;
			if (!GridIsSet(out message)) return message;

			string category, name;
			Description(selectedGrid as MyEntity, out category, out name);

			var md = new MyMarkdown();
			md.Append($"# Integrity Check {Quote(name)}");

			var damaged = new List<IMySlimBlock>();
			foreach (IMySlimBlock block in (selectedGrid as MyCubeGrid).CubeBlocks)
			{
				if (block.Integrity < block.MaxIntegrity)
					damaged.Add(block);
			}

			if (damaged.Count == 0)
			{
				md.Append("All blocks are intact.");
				return Success(md.Result());
			}

			var byCategory = new Dictionary<string, List<IMySlimBlock>>();
			foreach (var block in damaged)
			{
				var cat = Categorize(block.FatBlock as IMyTerminalBlock);
				List<IMySlimBlock> list;
				if (!byCategory.TryGetValue(cat, out list))
				{
					list = new List<IMySlimBlock>();
					byCategory[cat] = list;
				}
				list.Add(block);
			}

			foreach (var kv in byCategory)
			{
				StringBuilder sb = new StringBuilder();
				foreach (var block in kv.Value)
				{
					var p = block.Integrity / block.MaxIntegrity;
					sb.Append($"* {Quote(Name(block))} at ({IJK(block.Position)}) [{Percent(p)}]\n");
				}
				md.Add($"## {kv.Key}", sb.ToString());
			}

			return Success(md.Result());
		}

		internal CommandResult Points(TokenParser tp)
		{
			string message;
			if(!GridIsSet(out message)) return message;

			Vector3I ijk;
			if(!tp.NextVector3I(out ijk)) return "Error: expected I J K";

			var block = selectedGrid.GetCubeBlock(ijk);
			if(block == null) return $"Error: no block at {IJK(ijk)}";

			var sb = new StringBuilder();
			sb.Append($"Interaction points for {Name(block)} at {IJK(ijk)}:\n");
			AppendInteractionPoints(ijk, sb);
			return Success(sb.ToString());
		}

		internal CommandResult Near(TokenParser tp)
		{	
			string message;
			if(!GridIsSet(out message)) return message;

			var md = new MyMarkdown();

			Vector3I ijk;
			string hint;

			if(tp.End)
			{	ijk = selectedGrid.WorldToGridInteger(GetEngineerCenter());
				hint = "Your block";
			}
			else
			{	if(!tp.NextVector3I(out ijk)) return "Expected: I J K";
				hint = "Central block";
			}

			var name = Name(selectedGrid.GetCubeBlock(ijk));
			
			md.Append($"# {hint}: {Quote(name)} Position: ({IJK(ijk)})");

			positions.Clear();

			var min = ijk - Vector3I.One;
			var max = ijk + Vector3I.One;
			var iter = new Vector3I_RangeIterator(ref min, ref max);
			for (; iter.IsValid(); iter.MoveNext())
			{	positions.Add(iter.Current);
			}

			ListDescription(positions, false, md);

			return Success(md.Result());
		}

		private struct SearchMatch
		{
			public double Distance;
			public string Text;
		}

		internal CommandResult Search(TokenParser tp)
		{
			bool searchItems = false;
			bool searchBlocks = false;

			if (tp.Match("item")) searchItems = true;
			else if (tp.Match("block")) searchBlocks = true;
			else return "Error: expected 'item' or 'block'. e.g. `search item 'substring' [N]`";

			string query = tp.NextString();
			int limit;
			if (!tp.NextInt(out limit)) limit = 5;

			var engineer = GetEngineerCenter();
			var S = new BoundingSphereD(engineer, Constants.NearInformationRadius);

			var entities = MyEntities.GetTopMostEntitiesInSphere(ref S);

			List<SearchMatch> matches = new List<SearchMatch>();

			foreach (var e in entities)
			{
				var grid = e as IMyCubeGrid;
				if (grid == null || grid.Closed) continue;

				var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(grid);
				terminalBlocks.Clear();
				ts.GetBlocks(terminalBlocks);

				foreach (var block in terminalBlocks)
				{
					if (block.CubeGrid != grid) continue;

					string blockName = Name(block.SlimBlock);

					if(searchBlocks)
					{	if(Include(query, blockName))
						{	
							Vector3D wc;
							block.SlimBlock.ComputeWorldCenter(out wc);
							double distance = (wc - engineer).Length();
							
							matches.Add(new SearchMatch
							{
								Distance = distance,
								Text =
									$"* block {Quote(blockName)} at {IJK(block.Position)}" +
									$" on {Quote(Name(grid))} (distance {Distance(distance)})\n"
							});					
						}
					}
					
					if(searchItems && block.HasInventory)
					{	Vector3D wc;
						block.SlimBlock.ComputeWorldCenter(out wc);
						double distance = (wc - engineer).Length();

						for (int i = 0; i < block.InventoryCount; ++i)
						{
							var richInv = block.GetInventory(i) as WTF_IMyInventory;
							if (richInv == null) continue;

							foreach (var item in richInv.GetItems())
							{
								var contentId = item.Content.GetId();
								var itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(contentId);
								if (itemDef == null) continue;

								string itemName = itemDef.DisplayNameText;
								if(Include(query, itemName))
								{	matches.Add(new SearchMatch
									{
										Distance = distance,
										Text =
											$"* {Quote(itemName)} → {(double)item.Amount:F2}" +
											$" in block {Quote(blockName)} at {IJK(block.Position)}" +
											$" on {Quote(Name(grid))} (distance {Distance(distance)})\n"
									});
								}
							}
						}
					}
				}
				terminalBlocks.Clear();
			}

			matches.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			
			StringBuilder sb = new StringBuilder();
			int count = matches.Count;
			if (count > limit) matches.RemoveRange(limit, count - limit);

			string what = "";
			if(searchItems && searchBlocks) what = "items and blocks";
			else if(searchItems) what = "items";
			else if(searchBlocks) what = "blocks";

			sb.Append($"Found {count} {what} matching {Quote(query)}:\n");
			foreach (var m in matches) sb.Append(m.Text);

			return Success(sb.ToString());
		}

		internal CommandResult Slice(TokenParser tp)
		{
			string message;
			if(!GridIsSet(out message)) return message;

			int xmin, xmax, ymin, ymax, zmin, zmax;
			if(!tp.NextInt(out xmin) || !tp.NextInt(out xmax) || !tp.NextInt(out ymin) ||
			   !tp.NextInt(out ymax) || !tp.NextInt(out zmin) || !tp.NextInt(out zmax))
				return "Usage: slice Xmin Xmax Ymin Ymax Zmin Zmax";

			if(xmin > xmax || ymin > ymax || zmin > zmax)
				return "Invalid range: min must be <= max.";

			// Index by axis: 0=X, 1=Y, 2=Z
			int[] min   = { xmin, ymin, zmin };
			int[] max   = { xmax, ymax, zmax };
			int[] count = { xmax - xmin + 1, ymax - ymin + 1, zmax - zmin + 1 };

			// Find the fixed axis (thickness = 1); the other two form the table.
			int flatAxis = -1;
			for(int i = 0; i < 3; i++)
			{
				if(count[i] == 1) { flatAxis = i; break; }
			}
			if(flatAxis < 0)
				return "One axis must have height 1 (min == max).";

			// First remaining axis → columns, second → rows.
			int colAxis = -1, rowAxis = -1;
			for(int i = 0; i < 3; i++)
			{
				if(i == flatAxis) continue;
				if(colAxis < 0) colAxis = i; else rowAxis = i;
			}

			int cols = count[colAxis];
			int rows = count[rowAxis];
			if(cols > 10 || rows > 10)
				return $"Table dimensions ({cols}x{rows}) exceed 10x10 limit.";

			string[] axisNames = { "X", "Y", "Z" };
			var md = new MyMarkdown();
			md.Append($"# Slice ({axisNames[flatAxis]}={min[flatAxis]})");

			// Header row — show which axes form rows \ columns
			var header = new StringBuilder($"| {axisNames[rowAxis]}\\{axisNames[colAxis]} |");
			for(int c = 0; c < cols; c++)
				header.Append($" {min[colAxis] + c} |");
			md.Append(header.ToString());

			// Separator row
			var sep = new StringBuilder("|-----|");
			for(int c = 0; c < cols; c++)
				sep.Append("------|");
			md.Append(sep.ToString());

			// Data rows — coords[flatAxis] is fixed; colAxis and rowAxis vary.
			int[] coords = { 0, 0, 0 };
			coords[flatAxis] = min[flatAxis];

			for(int r = 0; r < rows; r++)
			{
				coords[rowAxis] = min[rowAxis] + r;
				var row = new StringBuilder($"| {coords[rowAxis]} |");

				for(int c = 0; c < cols; c++)
				{
					coords[colAxis] = min[colAxis] + c;
					var pos = new Vector3I(coords[0], coords[1], coords[2]);

					var block = selectedGrid.GetCubeBlock(pos);
					string name = block != null ? Name(block) : ".";
					row.Append($" {name} |");
				}
				md.Append(row.ToString());
			}

			return Success(md.Result());
		}

		bool CheckConveyorConnection(IMyCubeBlock from, IMyCubeBlock to)
		{
			string tempFrom = $"HACK{from.EntityId}";
			string tempTo = $"HACK{to.EntityId}";

			string oldFromName = from.Name;
			string oldToName = to.Name;

			try
			{
				from.Name = tempFrom;
				to.Name = tempTo;
				return MyVisualScriptLogicProvider.IsConveyorConnected(tempFrom, tempTo);
			}
			finally
			{	from.Name = oldFromName;
				to.Name = oldToName;
			}
		}

		public bool IsHydrogenReachable(IMyCubeBlock block, List<IMyTerminalBlock> terminalBlocks)
		{
			foreach (var b in terminalBlocks)
			{
				if (b.CubeGrid != block.CubeGrid) continue;

				var tank = b as IMyGasTank;
				if (tank != null)
				{
					var tankDef = b.SlimBlock.BlockDefinition as MyGasTankDefinition;
					if (tankDef == null || tankDef.StoredGasId != hydrogenId) continue;
					if (tank.FilledRatio <= 0f) continue;
					if (CheckConveyorConnection(block, b)) return true;
					continue;
				}

				var gen = b as IMyGasGenerator;
				if (gen != null && b.IsWorking)
				{
					var src = b.Components.Get<MyResourceSourceComponent>();
					if (src == null) continue;
					if (src.DefinedOutputByType(hydrogenId) <= 0f) continue;
					if (CheckConveyorConnection(block, b)) return true;
				}
			}
			return false;
		}

		internal CommandResult Distance(TokenParser tp)
		{
			string message;
			if(!GridIsSet(out message)) return message;

			var grid = selectedGrid;

			Vector3I a;
			if(!tp.NextVector3I(out a)) return "Error: expected I J K";

			Vector3I b;
			if(tp.NextVector3I(out b))
			{
				var wa = grid.GridIntegerToWorld(a);
				var wb = grid.GridIntegerToWorld(b);
				
				var d = (wa - wb).Length();
				
				var blockA = grid.GetCubeBlock(a);
				var blockB = grid.GetCubeBlock(b);
				
				return Success($"Distance from"
					+ $" {Quote(Name(blockA))} at {IJK(a)} to"
					+ $" {Quote(Name(blockB))} at {IJK(b)}: {Distance(d)}");
			}
			else
			{
				var we = GetEngineerCenter();
				var wa = grid.GridIntegerToWorld(a);
				
				var d = (wa - we).Length();
				var block = grid.GetCubeBlock(a);
				return Success($"Distance from you to {Quote(Name(block))} at {IJK(a)}: {Distance(d)}");
			}
		}

		internal bool GridHasPower(IMyCubeGrid grid)
		{	var dist = grid.ResourceDistributor;
			return dist != null && dist.ResourceState != MyResourceStateEnum.NoPower;
		}

		internal static Vector3D CalculateUpVector(IMyCubeGrid grid)
		{
			var cg = grid as MyCubeGrid;
			MatrixD m = cg.WorldMatrix;
			
			if (cg.HasMainCockpit())
			{	m = cg.MainCockpit.WorldMatrix;
			}
			else if (cg.HasMainRemoteControl())
			{	m = cg.MainRemoteControl.WorldMatrix;
			}
			else
			{	foreach (var b in cg.GetFatBlocks())
				{	if (b is IMyShipController)
					{	m = b.WorldMatrix;
        				break;
    				}
				}
			}

			return m.Up;
		}
	}
}
