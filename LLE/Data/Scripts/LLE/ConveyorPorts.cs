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

		// AllOrientations inherits this order, so equivalent rolls always answer the same one.
		public static readonly Base6Directions.Direction[] Six =
		{	Base6Directions.Direction.Right,    Base6Directions.Direction.Left,
			Base6Directions.Direction.Up,       Base6Directions.Direction.Down,
			Base6Directions.Direction.Backward, Base6Directions.Direction.Forward
		};

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

		// Read off the definition, not the worn model: an unwelded block wears a construction
		// model, and those carry no conveyor dummies at all.
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

				// MyConveyorLine.GetBlockLinePositions, verbatim.
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

		public static void OfBlock(IMySlimBlock block, List<ConveyorPort> result)
		{
			result.Clear();
			if (block == null) return;

			var def = block.BlockDefinition as MyCubeBlockDefinition;
			if (def == null) return;

			At(def, block.Orientation, block.Position, result);
		}

		/// <summary>The same for a block that is only planned — a draft entry has no IMySlimBlock.</summary>
		public static void At(MyCubeBlockDefinition def, MyBlockOrientation orientation, Vector3I position,
			List<ConveyorPort> result)
		{
			result.Clear();

			var local = Local(def);

			Matrix rotation;
			orientation.GetMatrix(out rotation);

			for (int i = 0; i < local.Length; ++i)
			{
				result.Add(new ConveyorPort
				{	Cell = Vector3I.Round(Vector3.Transform(new Vector3(local[i].Cell), rotation)) + position,
					Direction = orientation.TransformDirection(local[i].Direction)
				});
			}
		}

		public static void AtCell(IMySlimBlock block, Vector3I cell, List<Base6Directions.Direction> result)
		{
			result.Clear();

			var ports = new List<ConveyorPort>();
			OfBlock(block, ports);

			for (int i = 0; i < ports.Count; ++i)
				if (ports[i].Cell == cell) result.Add(ports[i].Direction);
		}

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
