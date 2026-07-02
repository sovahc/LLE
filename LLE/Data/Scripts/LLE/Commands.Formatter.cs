using System;
using System.Linq;

using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities;

namespace LLE
{
	public partial class Commands
	{
		public static string Remove_MyObjectBuilder_(string type)
		{
			if (type.StartsWith("MyObjectBuilder_")) type = type.Substring("MyObjectBuilder_".Length);
			return type;
		}

		public static string Quote(string s)
		{	if (s == null) return "(null)";
			if (!s.Contains(' ')) return s;
			return $"'{s}'";
		}

		public static string Distance(double d)
		{	if (d < 1000)
				return $"{(int)Math.Round(d, 0, MidpointRounding.AwayFromZero)}m";
			return $"{d / 1000.0:F1}km";
		}

		public static string Percent(float f)
		{	var ff = (int)Math.Round(f * 100, 0, MidpointRounding.AwayFromZero);
			return $"{ff}%";
		}

		public static string Volume(double d)
		{	var dd = Math.Round(d, 2, MidpointRounding.AwayFromZero);
			return $"{dd:F2}m³";
		}

		public static string IJK(Vector3I v)
		{	return $"{v.X} {v.Y} {v.Z}";
		}

		public static string BlockName(IMySlimBlock block)
		{	return block?.BlockDefinition?.DisplayNameText ?? "Unknown";
		}

		public static void Description(MyEntity e, out string category, out string name)
		{
			category = "Unknown";
			name = e.DisplayName;
			if(name == null) name = e.ToString();

			var grid = e as IMyCubeGrid;
			if (grid != null)
			{	if(grid.IsStatic) category = "STATION";
				else if(grid.GridSizeEnum == MyCubeSize.Large) category = "LARGE GRID";
				else if(grid.GridSizeEnum == MyCubeSize.Small) category = "SMALL GRID";
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
