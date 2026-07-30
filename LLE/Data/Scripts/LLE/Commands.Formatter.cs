using System;
using System.Linq;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;
using VRage.ModAPI;

namespace LLE
{
	public partial class Commands
	{
		public static string Remove_MyVoxelMap(string name)
		{
			string n0 = name;
			name = name.Replace("MyVoxelMap ", "");
			if(n0.Length != name.Length)
			{	name = name.Replace("{", "");
				name = name.Replace("}", "");
			}
			return name;
		}

		public static string Quote(string s)
		{	if (s == null) return "(null)";
			if (!s.Contains(' ')) return s;
			return $"'{s}'";
		}

		public static string Distance(double d)
		{	if (d < 1000)
				return $"{d:F1}m";
			return $"{d / 1000.0:F1}km";
		}

		public static string Percent(float f)
		{	return $"{f * 100:F1}%";
		}

		public static string Volume(double d)
		{	var dd = Math.Round(d, 2, MidpointRounding.AwayFromZero);
			return $"{dd:F2}m³";
		}

		public static string IJK(Vector3I v)
		{	return $"{v.X} {v.Y} {v.Z}";
		}

		// Name of an empty cell in listings; const so callers need not match a literal.
		public const string FreeSpace = "Free space";

		public static string Name(IMySlimBlock block)
		{
			if(block == null) return FreeSpace;
			return block.BlockDefinition.DisplayNameText;
		}

		public static string Name(IMyCubeGrid grid)
		{	return grid.CustomName ?? "Unnamed Grid";
		}

		// A projector's preview/hologram grid: no physics, no real blocks, nothing that
		// grind/weld/inventory/recharge/enter can meaningfully act on yet.
		public static bool IsProjection(IMyCubeGrid grid)
		{
			var mcg = grid as MyCubeGrid;
			return mcg != null && mcg.Projector != null;
		}

		public static void Description(IMyEntity e, out string category, out string name)
		{
			category = "Unknown";
			name = e.DisplayName;
			if(name == null) name = Remove_MyVoxelMap(e.ToString());

			var grid = e as IMyCubeGrid;
			if (grid != null)
			{	if(grid.IsStatic) category = "STATION";
				else if(grid.GridSizeEnum == MyCubeSize.Large) category = "LARGE GRID";
				else if(grid.GridSizeEnum == MyCubeSize.Small) category = "SMALL GRID";

				if(IsProjection(grid)) category += " (PROJECTION)";
				return;
			}
			var voxel = e as MyVoxelBase;
			if (voxel != null)
			{	if (voxel is MyPlanet)
				{	category = "PLANET";
					return;
				}
				category = "ASTEROID";
				return;
			}

			var floater = e as IMyFloatingObject;
			if (floater != null)
			{	category = "FLOATING OBJECT";
				return;
			}
		}
	}
}
