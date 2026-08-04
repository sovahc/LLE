using System;
using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Definitions;

namespace LLE
{
	struct ConveyorPort
	{
		/// <summary>Which cell the port sits on: block-local offset from Local(), grid cell from OfBlock().</summary>
		public Vector3I Cell;
		public Base6Directions.Direction Direction;
	}

	static class ConveyorPorts
	{
		private static readonly ConveyorPort[] None = new ConveyorPort[0];

		private static readonly Dictionary<MyDefinitionId, ConveyorPort[]> localCache =
			new Dictionary<MyDefinitionId, ConveyorPort[]>();

		// +X -X +Y -Y +Z -Z, the order the model reads and writes directions in. AllOrientations
		// inherits it, so where several orientations put the ports in the same place — the four
		// rolls of a straight tube around its own axis, the two of an elbow — the one answered is
		// always the same one, and it is the one with `facing` earliest in this list. Checked
		// against the orientation tables this replaced: every line of theirs is reproduced or
		// answered with an equivalent roll, none with a wrong `up`.
		public static readonly Base6Directions.Direction[] Six =
		{	Base6Directions.Direction.Right,    Base6Directions.Direction.Left,
			Base6Directions.Direction.Up,       Base6Directions.Direction.Down,
			Base6Directions.Direction.Backward, Base6Directions.Direction.Forward
		};

		// The 24 orientations a cube block can be built in.
		public static readonly MyBlockOrientation[] AllOrientations = BuildOrientations();

		private static MyBlockOrientation[] BuildOrientations()
		{
			var list = new List<MyBlockOrientation>();
			for (int f = 0; f < Six.Length; ++f)
				for (int u = 0; u < Six.Length; ++u)
					if (Base6Directions.IsValidBlockOrientation(Six[f], Six[u]))
						list.Add(new MyBlockOrientation(Six[f], Six[u]));
			return list.ToArray();
		}

		private static void AddOnce(List<ConveyorPort> list, Vector3I cell, Base6Directions.Direction d)
		{
			for (int i = 0; i < list.Count; ++i)
				if (list[i].Cell == cell && list[i].Direction == d) return;

			list.Add(new ConveyorPort { Cell = cell, Direction = d });
		}

		/// <summary>Ports in the block's own coordinates. Empty when the block has none.</summary>
		public static ConveyorPort[] Local(MyCubeBlockDefinition def)
		{
			ConveyorPort[] cached;
			if (localCache.TryGetValue(def.Id, out cached)) return cached;

			var ports = FromDetectors(def);
			localCache[def.Id] = ports;
			return ports;
		}

		public static bool HasPorts(MyCubeBlockDefinition def)
		{	return Local(def).Length != 0;
		}

		// The exported dummies of the block's own model — the same source the game reads in
		// MyConveyorLine.GetBlockLinePositions, and read the same way. It answers off the
		// definition, which is what makes a freshly placed block answer like a finished one: the
		// model an unwelded block actually wears is a construction model, and those carry no
		// conveyor dummies at all. Blocks missing from collisions.bin — mods — get no ports.
		private static ConveyorPort[] FromDetectors(MyCubeBlockDefinition def)
		{
			CollisionGeometry geometry;
			if (!Collisions._collisionGeometry.TryGetValue(def.Id, out geometry)) return None;

			float cubeSize = MyDefinitionManager.Static.GetCubeSize(def.CubeSize);
			Vector3 half = new Vector3(def.Size) * 0.5f * cubeSize;

			var found = new List<ConveyorPort>();

			for (int i = 0; i < geometry.Detectors.Count; ++i)
			{
				var detector = geometry.Detectors[i];
				if (!detector.Name.StartsWith("conveyor", StringComparison.OrdinalIgnoreCase)) continue;

				// GetBlockLinePositions, verbatim: which cell of the block the dummy sits in, then
				// its offset from that cell's centre snapped to the dominant axis.
				Vector3 p = detector.Transform.Translation + def.ModelOffset + half;
				Vector3I cell = Vector3I.Min(
					Vector3I.Max(Vector3I.Floor(p / cubeSize), Vector3I.Zero),
					def.Size - Vector3I.One);

				Vector3 centre = (new Vector3(cell) + Vector3.Half) * cubeSize;
				Vector3 v = Vector3.Normalize(Vector3.DominantAxisProjection((p - centre) / cubeSize));

				AddOnce(found, cell - def.Center, Base6Directions.GetDirection(v));
			}

			return found.Count == 0 ? None : found.ToArray();
		}

		/// <summary>Ports of a block standing on the grid: grid cells and grid directions.</summary>
		public static void OfBlock(IMySlimBlock block, List<ConveyorPort> result)
		{
			result.Clear();
			if (block == null) return;

			var def = block.BlockDefinition as MyCubeBlockDefinition;
			if (def == null) return;

			var local = Local(def);

			var orientation = block.Orientation;
			Matrix rotation;
			orientation.GetMatrix(out rotation);

			for (int i = 0; i < local.Length; ++i)
			{
				result.Add(new ConveyorPort
				{	Cell = Vector3I.Round(Vector3.Transform(new Vector3(local[i].Cell), rotation)) + block.Position,
					Direction = orientation.TransformDirection(local[i].Direction)
				});
			}
		}

		/// <summary>Directions of a block's ports at one particular cell of it.</summary>
		public static void AtCell(IMySlimBlock block, Vector3I cell, List<Base6Directions.Direction> result)
		{
			result.Clear();

			var ports = new List<ConveyorPort>();
			OfBlock(block, ports);

			for (int i = 0; i < ports.Count; ++i)
				if (ports[i].Cell == cell) result.Add(ports[i].Direction);
		}

		/// <summary>Port directions a 1x1x1 block would have if built in this orientation.</summary>
		public static void InOrientation(MyCubeBlockDefinition def, MyBlockOrientation orientation,
			List<Base6Directions.Direction> result)
		{
			result.Clear();
			var local = Local(def);
			for (int i = 0; i < local.Length; ++i)
				result.Add(orientation.TransformDirection(local[i].Direction));
		}

		public static bool Contains(List<Base6Directions.Direction> list, Base6Directions.Direction d)
		{
			for (int i = 0; i < list.Count; ++i) if (list[i] == d) return true;
			return false;
		}
	}
}
