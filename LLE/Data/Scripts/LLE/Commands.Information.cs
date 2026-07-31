using System;
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

		// Was "Name → count (positions on the grid)": 23% of Gemma's answers read that as
		// a menu of free slots and built inside the hull. `at` is the spelling that works
		// (140/140), and the relation is now stated, not hinted — the shipping mode does
		// not think. Measured 2026-07-30, ~/Projects/GemmaBuilder.
		internal void ListDescription(List<Vector3I> coordinates, bool byCategory, MyMarkdown md)
		{	md.Append($"Legend: `Name at P1; P2; ...` means a block called Name stands at each of those positions."
				+ $" `{FreeSpace} at ...` means those cells are empty.");

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
					var terminalBlock = cubeBlock?.FatBlock as IMyTerminalBlock;
					if (terminalBlock != null)
						nameToSample[name] = terminalBlock;
				}
				pp.Add(position);
			}

			if(describer.Count == 0)
				md.Append(" -- none --");

			foreach (var kv in describer)
			{
				var name = kv.Key;
				IMyTerminalBlock sample;
				var category = byCategory && nameToSample.TryGetValue(name, out sample) ? Categorize(sample) : null;

				StringBuilder sb = new StringBuilder();
				sb.Append($"* {Quote(kv.Key)} at ");

				bool semi = false;
				foreach(var p in kv.Value)
				{	if(semi) sb.Append("; ");
					sb.Append(IJK(p));
					semi = true;
				}
				// count last: nothing between the name and the positions
				sb.Append(kv.Key == FreeSpace ? $" ({kv.Value.Count} cells)" : $" ({kv.Value.Count} blocks)");

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

			if(IsProjection(selectedGrid))
			{
				md.Append("This is a projector's projection preview, not a built object.");
				md.Append("Use `projection` for build status.");
				return Success(md.Result());
			}

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
				md.Append($"Dampeners enabled: {cg.DampenersEnabled}");

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

		// Compact, bot-facing build status for a projection preview: counts plus the actual
		// list of what's buildable right now. Deliberately does not dump the "blocked" list —
		// it's not actionable and would be pure context noise for a bot polling this repeatedly.
		internal CommandResult Projection()
		{
			string message;
			if(!GridIsSet(out message)) return message;

			if(!IsProjection(selectedGrid))
				return "Error: selected grid is not a projection.";

			var mcg = selectedGrid as MyCubeGrid;
			var projector = mcg.Projector as IMyProjector;
			if(projector == null)
				return "Error: could not find the projector owning this projection.";

			var realGrid = projector.CubeGrid;

			var md = new MyMarkdown();
			md.Append($"Real grid (select this to weld already-placed blocks): {Quote(Name(realGrid))}");
			md.Append($"Total blocks: {projector.TotalBlocks}");
			md.Append($"Not yet built: {projector.RemainingBlocks}");
			md.Append($"Buildable now: {projector.BuildableBlocksCount}");

			if(projector.BuildableBlocksCount > 0)
			{
				var allBlocks = new List<IMySlimBlock>();
				selectedGrid.GetBlocks(allBlocks);

				var buildable = new List<Vector3I>();
				foreach(var ghost in allBlocks)
				{
					// CanBuild's AlreadyBuilt result isn't reliable for hidden (already-built) ghosts —
					// rule those out by direct position + definition match first, per ProjectorInterface.md.
					var worldPos = selectedGrid.GridIntegerToWorld(ghost.Position);
					var realBlock = realGrid.GetCubeBlock(realGrid.WorldToGridInteger(worldPos));
					if(realBlock != null && realBlock.BlockDefinition.Id == ghost.BlockDefinition.Id)
						continue; // already built

					if(projector.CanBuild(ghost, false) == BuildCheckResult.OK)
						buildable.Add(ghost.Position);
				}

				md.Append($"### Buildable now");
				ListDescription(buildable, false, md);
			}

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

			List<IMySlimBlock> damaged;

			if(IsProjection(selectedGrid))
			{
				var mcg = selectedGrid as MyCubeGrid;
				var projector = mcg.Projector as IMyProjector;
				if(projector == null)
					return "Error: could not find the projector owning this projection.";

				var realGrid = projector.CubeGrid;

				var ghosts = new List<IMySlimBlock>();
				selectedGrid.GetBlocks(ghosts);

				damaged = new List<IMySlimBlock>();
				foreach(var ghost in ghosts)
				{
					// CanBuild's AlreadyBuilt result isn't reliable for this (hidden ghosts don't report it) —
					// match position + definition against the real grid directly, per ProjectorInterface.md.
					var worldPos = selectedGrid.GridIntegerToWorld(ghost.Position);
					var realBlock = realGrid.GetCubeBlock(realGrid.WorldToGridInteger(worldPos));
					if(realBlock == null || realBlock.BlockDefinition.Id != ghost.BlockDefinition.Id)
						continue; // not built yet, that's `projection`'s job

					if(realBlock.Integrity < realBlock.MaxIntegrity)
						damaged.Add(realBlock);
				}

				if(damaged.Count == 0)
				{
					md.Append("All built blocks are intact. (Use `projection` to see what's still unbuilt.)");
					return Success(md.Result());
				}
			}
			else
			{
				damaged = new List<IMySlimBlock>();
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
			}

			var byCategory = new Dictionary<string, List<IMySlimBlock>>();
			foreach (var block in damaged)
			{
				string cat;
				if(block.FatBlock != null && block.FatBlock is IMyTerminalBlock)
					cat = Categorize(block.FatBlock as IMyTerminalBlock);
				else
					cat = "Other";
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
			{	if(!tp.NextVector3I(out ijk)) return "Error: Expected: I J K";
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
			public bool Exact;
		}

		// How many query words may go unmatched and a name still counts as "partial".
		// 0 = only all-words-present names qualify (see LLE-search-fix.md). Raise to admit
		// looser matches like "3 of 4 words".
		private const int SearchMinWordsSlack = 0;

		private static string[] SearchWords(string s)
		{
			var raw = s.ToLowerInvariant().Split(' ');
			var words = new List<string>(raw.Length);
			foreach (var w in raw) if (w.Length > 0) words.Add(w);
			return words.ToArray();
		}

		private static int WordsMatched(string[] queryWords, string[] textWords)
		{
			int count = 0;
			foreach (var qw in queryWords)
			{
				foreach (var tw in textWords)
				{
					if (tw == qw) { count++; break; }
				}
			}
			return count;
		}

		// Classifies `text` against `query`: exact (contiguous substring, case-insensitive),
		// partial (all-but-slack query words present as whole words), or excluded (returns false).
		// On a match, `tag` is set to what to show: "[exact]", "[N/N]", or "[*]" for wildcard.
		private static bool ClassifyMatch(string query, string[] queryWords, string text, out bool exact, out string tag)
		{
			exact = false;
			tag = null;

			if (string.IsNullOrEmpty(query) || query == "*")
			{	tag = "[*]";
				return true;
			}

			if (text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
			{	exact = true;
				tag = "[exact]";
				return true;
			}

			var textWords = SearchWords(text);
			int matched = WordsMatched(queryWords, textWords);
			int threshold = System.Math.Max(1, queryWords.Length - SearchMinWordsSlack);
			if (matched >= threshold)
			{	tag = $"[{matched}/{queryWords.Length}]";
				return true;
			}

			return false;
		}

		internal CommandResult Search(TokenParser tp)
		{
			bool searchItems = false;
			bool searchBlocks = false;

			if (tp.Match("item")) searchItems = true;
			else if (tp.Match("block")) searchBlocks = true;
			else return "Error: expected 'item' or 'block'. e.g. `search item 'substring' [N]`";

			string query = tp.NextString();
			var queryWords = SearchWords(query);
			int limit;
			if (!tp.NextInt(out limit)) limit = 5;

			var engineer = GetEngineerCenter();
			var S = new BoundingSphereD(engineer, Constants.NearInformationRadius);

			var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref S);

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
					{	bool exact;
						string tag;
						if(ClassifyMatch(query, queryWords, blockName, out exact, out tag))
						{
							Vector3D wc;
							block.SlimBlock.ComputeWorldCenter(out wc);
							double distance = (wc - engineer).Length();

							matches.Add(new SearchMatch
							{
								Distance = distance,
								Exact = exact,
								Text =
									$"* {tag} block {Quote(blockName)} at {IJK(block.Position)}" +
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
								bool exact;
								string tag;
								if(ClassifyMatch(query, queryWords, itemName, out exact, out tag))
								{	matches.Add(new SearchMatch
									{
										Distance = distance,
										Exact = exact,
										Text =
											$"* {tag} {Quote(itemName)} → {(double)item.Amount:F2}" +
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

			var exactMatches = matches.FindAll(m => m.Exact);
			var partialMatches = matches.FindAll(m => !m.Exact);

			int partialShown = System.Math.Min(limit, partialMatches.Count);
			int partialDropped = partialMatches.Count - partialShown;

			var shown = new List<SearchMatch>(exactMatches);
			shown.AddRange(partialMatches.GetRange(0, partialShown));
			shown.Sort((a, b) => a.Distance.CompareTo(b.Distance));

			StringBuilder sb = new StringBuilder();

			string what = "";
			if(searchItems && searchBlocks) what = "items and blocks";
			else if(searchItems) what = "items";
			else if(searchBlocks) what = "blocks";

			string qualifier = partialDropped > 0 ? $" (showing {partialShown} closest partial)" : "";
			sb.Append($"Found {matches.Count} {what} matching {Quote(query)}" +
				$" ({exactMatches.Count} exact, {partialMatches.Count} partial){qualifier}:\n");
			foreach (var m in shown) sb.Append(m.Text);

			return Success(sb.ToString());
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

			if(!tp.Match("from")) return "Error: expected from'";

			Vector3I a;
			if(!tp.NextVector3I(out a)) return "Error: expected I J K";

			if(!tp.Match("to")) return "Error: expected 'to'";

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
		{	return CalculateOrientation(grid).Up;
		}

		internal static MatrixD CalculateOrientation(IMyCubeGrid grid)
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

			return m;
		}

		internal static string GridDirections(IMyCubeGrid grid)
		{
			var m = CalculateOrientation(grid);
			var toLocal = grid.PositionComp.WorldMatrixNormalizedInv;

			return $"up = {Axis(m.Up, ref toLocal)}, down = {Axis(m.Down, ref toLocal)}"
				+ $", forward = {Axis(m.Forward, ref toLocal)}, backward = {Axis(m.Backward, ref toLocal)}"
				+ $", left = {Axis(m.Left, ref toLocal)}, right = {Axis(m.Right, ref toLocal)}";
		}

		private static string Axis(Vector3D worldDir, ref MatrixD toLocal)
		{
			var v = Vector3D.TransformNormal(worldDir, toLocal);

			double ax = System.Math.Abs(v.X), ay = System.Math.Abs(v.Y), az = System.Math.Abs(v.Z);

			if (ax >= ay && ax >= az) return Dir(new Vector3I(System.Math.Sign(v.X), 0, 0));
			if (ay >= az)             return Dir(new Vector3I(0, System.Math.Sign(v.Y), 0));
			return Dir(new Vector3I(0, 0, System.Math.Sign(v.Z)));
		}
	}
}
