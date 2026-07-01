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

		internal void ListDescription(List<Vector3I> coordinates, bool byCategory, MyMarkdown md)
		{	md.Append($"Legend: Name → count (positions on the grid)");

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
					md.Add($"## {category}", sb.ToString());
				else
					md.Append(sb.ToString());
			}

			describer.Clear();
		}

		internal string Overview()
		{
			string message;
			if(!GridIsSet(out message)) return message;

			string category, name;
			Description(selectedGrid as MyEntity, out category, out name);

			var md = new MyMarkdown();
			md.Append($"# {category} '{name}'");

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

			return md.Result();
		}

		internal string Integrity()
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
				return md.Result();
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
				md.Add($"## {kv.Key}", sb.ToString());
			}

			return md.Result();
		}

		internal string Near(TokenParser tp)
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

			return md.Result();
		}

		internal string Slice(TokenParser tp)
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
			if(cols > 8 || rows > 8)
				return $"Table dimensions ({cols}x{rows}) exceed 8x8 limit.";

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

			return md.Result();
		}
	}
}
