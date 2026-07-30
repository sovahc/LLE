using System.Collections.Generic;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;

// Conveyor port geometry. There is no port data in a block definition — in this engine a port
// is a model dummy named `detector_conveyor*`, and the game reads those in
// MyConveyorLine.GetBlockLinePositions (Sandbox.Game.decompiled.cs:313961).
//
// That method, and MyModels with it, is outside the mod whitelist, so the dummies are read
// through the mod API instead: IMyModel.GetDummies, the same call the published LeakFinder and
// RealEnergy mods use. It needs a block that is actually standing, so ports are learned from
// real blocks and cached per definition — which is enough, because every block a run is routed
// to or from is on the grid by definition.
//
// The one case with no instance to learn from is the tube about to be built. For a conveyor
// tube or junction — and only for those two object builder types — the mount points are the
// ports: the block attaches by exactly the faces its ports are on. Checked in
// CubeBlocks_Logistics.sbc: ConveyorTube mounts Top+Bottom, ConveyorTubeCurved Bottom+Right,
// ConveyorTubeSmall Back+Front (so this is not a fact about `up`), LargeBlockConveyor carries
// no MountPoints element and MyCubeBlockDefinition.InitMountPoints then generates all six.

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

		private static readonly Dictionary<string, IMyModelDummy> dummies =
			new Dictionary<string, IMyModelDummy>();

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

		/// <summary>Ports in the block's own coordinates. Empty when they are not known.</summary>
		public static ConveyorPort[] Local(MyCubeBlockDefinition def)
		{
			ConveyorPort[] cached;
			if (localCache.TryGetValue(def.Id, out cached)) return cached;

			var fromMounts = FromMountPoints(def);
			if (fromMounts != null)
			{	localCache[def.Id] = fromMounts;
				return fromMounts;
			}

			return None; // nothing built yet to learn from
		}

		public static bool HasPorts(MyCubeBlockDefinition def)
		{	return Local(def).Length != 0;
		}

		// Conveyor tubes and junctions only — for anything else a mount face says nothing about
		// a port, and a reactor would come out claiming ports on all six.
		private static ConveyorPort[] FromMountPoints(MyCubeBlockDefinition def)
		{
			if (def.Size != Vector3I.One) return null;

			if (def.Id.TypeId != typeof(MyObjectBuilder_Conveyor) &&
				def.Id.TypeId != typeof(MyObjectBuilder_ConveyorConnector)) return null;

			if (def.MountPoints == null || def.MountPoints.Length == 0) return null;

			var found = new List<ConveyorPort>();

			for (int i = 0; i < def.MountPoints.Length; ++i)
			{
				if (!def.MountPoints[i].Enabled) continue;
				AddOnce(found, Vector3I.Zero, Commands.DirOf(def.MountPoints[i].Normal));
			}

			return found.Count == 0 ? null : found.ToArray();
		}

		// Exact: the dummies of the block that is standing there. Returns null when the model has
		// none to offer, which also happens on a half-welded block — its construction model
		// carries no conveyor dummies, and an empty answer from one must not be cached.
		private static ConveyorPort[] FromModel(IMySlimBlock block, MyCubeBlockDefinition def)
		{
			var fat = block.FatBlock;
			if (fat == null || fat.Model == null) return null;

			dummies.Clear();
			if (fat.Model.GetDummies(dummies) == 0) return null;

			float cubeSize = MyDefinitionManager.Static.GetCubeSize(def.CubeSize);
			Vector3 half = new Vector3(def.Size) * 0.5f * cubeSize;

			var found = new List<ConveyorPort>();

			foreach (var dummy in dummies)
			{
				var parts = dummy.Key.ToLower().Split('_');
				if (parts.Length < 2 || parts[0] != "detector" || !parts[1].StartsWith("conveyor")) continue;

				// GetBlockLinePositions, verbatim: which cell of the block the dummy sits in, then
				// its offset from that cell's centre snapped to the dominant axis.
				Vector3 p = dummy.Value.Matrix.Translation + def.ModelOffset + half;
				Vector3I cell = Vector3I.Min(
					Vector3I.Max(Vector3I.Floor(p / cubeSize), Vector3I.Zero),
					def.Size - Vector3I.One);

				Vector3 centre = (new Vector3(cell) + Vector3.Half) * cubeSize;
				Vector3 v = Vector3.Normalize(Vector3.DominantAxisProjection((p - centre) / cubeSize));

				AddOnce(found, cell - def.Center, Base6Directions.GetDirection(v));
			}

			dummies.Clear();

			if (found.Count != 0) return found.ToArray();

			// Genuinely no ports — but only believe it once the block is finished.
			return block.Integrity >= block.MaxIntegrity ? None : null;
		}

		/// <summary>Ports of a block standing on the grid: grid cells and grid directions.</summary>
		public static void OfBlock(IMySlimBlock block, List<ConveyorPort> result)
		{
			result.Clear();
			if (block == null) return;

			var def = block.BlockDefinition as MyCubeBlockDefinition;
			if (def == null) return;

			ConveyorPort[] local;
			if (!localCache.TryGetValue(def.Id, out local))
			{
				local = FromModel(block, def);
				if (local != null) localCache[def.Id] = local;
				else local = Local(def); // mount points, or nothing
			}

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

		// --- the two run pieces ------------------------------------------------------------

		// Vanilla subtypes, preferred when present. The geometric test below is the real
		// criterion, so a mod's own tube is found too; the list only makes the choice stable.
		private static readonly string[] PreferredStraight = { "ConveyorTube", "ConveyorTubeSmall" };
		private static readonly string[] PreferredCurved = { "ConveyorTubeCurved", "ConveyorTubeSmallCurved" };

		private static readonly Dictionary<int, MyCubeBlockDefinition> pieceCache =
			new Dictionary<int, MyCubeBlockDefinition>();

		/// <summary>The 1x1x1 two-port piece for a run: straight, or a 90-degree bend.</summary>
		public static MyCubeBlockDefinition FindPiece(MyCubeSize size, bool curved)
		{
			int key = (int)size * 2 + (curved ? 1 : 0);

			MyCubeBlockDefinition cached;
			if (pieceCache.TryGetValue(key, out cached)) return cached;

			var preferred = curved ? PreferredCurved : PreferredStraight;

			MyCubeBlockDefinition best = null;

			foreach (var d in MyDefinitionManager.Static.GetAllDefinitions())
			{
				var def = d as MyCubeBlockDefinition;
				if (def == null || !def.Public) continue;
				if (def.CubeSize != size) continue;
				if (def.Size != Vector3I.One) continue;

				var ports = Local(def);
				if (ports.Length != 2) continue;

				bool opposite = ports[0].Direction == Base6Directions.GetFlippedDirection(ports[1].Direction);
				if (opposite == curved) continue; // straight wants opposite ports, curved wants perpendicular

				for (int i = 0; i < preferred.Length; ++i)
					if (def.Id.SubtypeName == preferred[i])
					{	pieceCache[key] = def;
						return def;
					}

				// no preferred subtype yet — keep the first by name, so the answer is stable
				if (best == null || string.CompareOrdinal(def.Id.SubtypeName, best.Id.SubtypeName) < 0)
					best = def;
			}

			pieceCache[key] = best;
			return best;
		}
	}
}
