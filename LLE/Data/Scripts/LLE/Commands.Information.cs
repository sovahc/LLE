using System.Collections.Generic;
using System.Linq;
using System.Text;

using VRageMath;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;

namespace LLE
{
	public partial class Commands
	{	
		private readonly List<string> ALL_COMPONENTS = new List<string>();
		
		private static readonly List<IMyTerminalBlock> terminalBlocks = new List<IMyTerminalBlock>();
		private static readonly Dictionary<string, List<Vector3I>> describer = new Dictionary<string, List<Vector3I>>();
		private static readonly List<Vector3I> positions = new List<Vector3I>();

		private static readonly Dictionary<string, string[]> TerminalBCategories = new Dictionary<string, string[]>
		{
			{ "Control", new[] { "Cockpit" } },
			{ "Energy", new[] { "Reactor", "Battery", "SolarPanel" } },
			{ "Defense", new[] { "Turret", "Warhead", "Decoy" } },
			{ "Construction", new[] { "ShipGrinder", "ShipWelder" } },
			{ "Mining", new[] { "OreDetector", "ShipDrill" } },
			{ "Communication", new[] { "Antenna", "Transponder" } }, // << ?
			{ "Production", new[] { "Refinery", "Assembler", "UpgradeModule" } },
			{ "Docking", new[] { "Connector", "Collector" } },
			{ "Gas", new[] { "OxygenGenerator", "OxygenTank", "AirVent" } },
			{ "Life Support", new[] { "CryoChamber", "MedicalRoom" } },
			{ "Computers", new[] { "EventController", "Timer", "BroadcastController", "TurretControl", "Sensor" } },
			{ "Doors", new[] { "Door" } },
			{ "Gravity", new[] { "GravityGenerator", "VirtualMass", "SpaceBall" } },
			{ "Rotors", new[] { "MotorAdvancedStator", "MotorStator", "Hinge" } },
			{ "Movement", new[] { "Thrust" } },
			{ "Storage", new[] { "CargoContainer" } },
			{ "Decoration", new[] { "HeatVent", "LCDPanel", "Terminal" } }
		};

		internal static string NameToCategory(string name)
		{	foreach (var cat in TerminalBCategories)
			{
				if (cat.Value.Any(keyword => name.Contains(keyword)))
				{
					return cat.Key;
				}
			}
			return "Other";
		}

		public static string Name(IMySlimBlock block)
		{
			if(block == null) return "Free space";
			return block.BlockDefinition.DisplayNameText;
		}

		internal void ListDescription(List<Vector3I> coordinates, string firstLine, bool byCategory)
		{	MyMarkdown.Clear();
			MyMarkdown.Append(firstLine);
			MyMarkdown.Append($"Legend: Name → count (positions on the grid)");

			describer.Clear();

			foreach (var position in coordinates)
			{	
				var name = Name(selectedGrid.GetCubeBlock(position));

				List<Vector3I> pp;
				if(!describer.TryGetValue(name, out pp))
				{	pp = new List<Vector3I>();
					describer[name] = pp;
				}
				pp.Add(position);
			}

			foreach (var kv in describer)
			{	
				var name = kv.Key;
				var category = byCategory ? NameToCategory(name) : null;

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
					MyMarkdown.Add($"## {category}", sb.ToString());
				else
					MyMarkdown.Append(sb.ToString());
			}

			describer.Clear();
		}

		internal string Overview()
		{
			string message;
			if(!GridIsSet(out message)) return message;

			string category, name;
			Description(selectedGrid as MyEntity, out category, out name);

			string firstLine = $"# {category} '{name}'";

			var ts = MyAPIGateway.TerminalActionsHelper.GetTerminalSystemForGrid(selectedGrid);
			terminalBlocks.Clear();
			ts.GetBlocks(terminalBlocks);

			positions.Clear();
			foreach(var block in terminalBlocks) positions.Add(block.SlimBlock.Position);

			ListDescription(positions, firstLine, true);

			terminalBlocks.Clear();
			positions.Clear();

			return MyMarkdown.Result();
		}

		internal string Integrity()
		{
			string message;
			if (!GridIsSet(out message)) return message;

			string category, name;
			Description(selectedGrid as MyEntity, out category, out name);

			MyMarkdown.Clear();
			MyMarkdown.Append($"# Integrity Check {Quote(name)}");

			var damaged = new List<IMySlimBlock>();
			foreach (IMySlimBlock block in (selectedGrid as MyCubeGrid).CubeBlocks)
			{
				if (block.Integrity < block.MaxIntegrity)
					damaged.Add(block);
			}

			if (damaged.Count == 0)
			{
				MyMarkdown.Append("All blocks are intact.");
				return MyMarkdown.Result();
			}

			var byCategory = new Dictionary<string, List<IMySlimBlock>>();
			foreach (var block in damaged)
			{
				var cat = NameToCategory(Name(block));
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
				MyMarkdown.Add($"## {kv.Key}", sb.ToString());
			}

			return MyMarkdown.Result();
		}

		internal string Near(TokenParser tp)
		{	
			string message;
			if(!GridIsSet(out message)) return message;

			MyMarkdown.Clear();

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
			
			string firstLine = $"# {hint}: {Quote(name)} Position: ({IJK(ijk)})";

			positions.Clear();

			foreach (var direction in Constants.SixDirections)
			{	positions.Add(ijk + direction);
			}

			ListDescription(positions, firstLine, false);

			return MyMarkdown.Result();
		}
	}
}
