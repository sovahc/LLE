using System.Collections;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using Sandbox.Definitions;
using Sandbox.Common.ObjectBuilders;

using Direction = VRageMath.Base6Directions.Direction;

namespace LLE
{
	public partial class Commands
	{
		// One buildable tube: which two directions its openings look along in its own coordinates.
		// Read off the game's own prefabs — several thousand built tubes agree on every line — and
		// not off the mount points, which lie: a reinforced duct declares mount points on all six
		// faces and has openings on two of them.
		private struct TubeVariant
		{
			public readonly string Family;
			public readonly MyCubeSize Size;
			public readonly bool Curved;
			public readonly string Subtype;
			public readonly Direction PortA, PortB;

			public TubeVariant(string family, MyCubeSize size, bool curved, string subtype,
				Direction portA, Direction portB)
			{	Family = family; Size = size; Curved = curved; Subtype = subtype;
				PortA = portA; PortB = portB;
			}
		}

		private const string ConveyorUsage =
			"Usage: place conveyor I J K D D₂ [square|round|reinforced], each D one of +X -X +Y -Y +Z -Z";

		private static readonly TubeVariant[] TubeVariants =
		{	new TubeVariant("square",     MyCubeSize.Large, false, "ConveyorTube",                   Direction.Up,       Direction.Down),
			new TubeVariant("square",     MyCubeSize.Large, true,  "ConveyorTubeCurved",             Direction.Right,    Direction.Down),
			new TubeVariant("square",     MyCubeSize.Small, false, "ConveyorTubeSmall",              Direction.Forward,  Direction.Backward),
			new TubeVariant("square",     MyCubeSize.Small, true,  "ConveyorTubeSmallCurved",        Direction.Backward, Direction.Down),

			new TubeVariant("reinforced", MyCubeSize.Large, false, "ConveyorTubeDuct",               Direction.Right,    Direction.Left),
			new TubeVariant("reinforced", MyCubeSize.Large, true,  "ConveyorTubeDuctCurved",         Direction.Right,    Direction.Down),
			new TubeVariant("reinforced", MyCubeSize.Small, false, "ConveyorTubeDuctSmall",          Direction.Forward,  Direction.Backward),
			new TubeVariant("reinforced", MyCubeSize.Small, true,  "ConveyorTubeDuctSmallCurved",    Direction.Backward, Direction.Down),

			// Heavy Industry pack, large grid only.
			new TubeVariant("round",      MyCubeSize.Large, false, "LargeBlockConveyorPipeSeamless", Direction.Up,       Direction.Down),
			new TubeVariant("round",      MyCubeSize.Large, true,  "LargeBlockConveyorPipeCorner",   Direction.Right,    Direction.Down)
		};

		// The orientation that puts this tube's two openings on the two asked-for directions.
		// Several orientations do — the rolls of the tube around itself — and they are the same
		// conveyor, so the first one wins.
		private static bool FindTubeOrientation(TubeVariant tube, Direction port1, Direction port2,
			out MyBlockOrientation orientation)
		{
			foreach (var candidate in ConveyorPorts.AllOrientations)
			{
				var a = candidate.TransformDirection(tube.PortA);
				var b = candidate.TransformDirection(tube.PortB);

				if ((a == port1 && b == port2) || (a == port2 && b == port1))
				{	orientation = candidate;
					return true;
				}
			}

			orientation = new MyBlockOrientation();
			return false;
		}

		internal IEnumerator PlaceConveyor(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;
			if (CurrentGridIsProjection(out message)) yield return message;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk))
				yield return "Error: expected I J K after `conveyor`. " + ConveyorUsage;

			Direction port1, port2;

			var s1 = tp.NextString();
			if (!ParseDirection(s1, out port1))
				yield return $"Error: {Quote(s1)} is not a direction. " + ConveyorUsage;

			var s2 = tp.NextString();
			if (!ParseDirection(s2, out port2))
				yield return $"Error: {Quote(s2)} is not a direction. " + ConveyorUsage;

			if (port1 == port2)
				yield return "Error: the two openings must look in different directions."
					+ " A tube has one opening on each end.";

			var family = "square";
			if (!tp.End)
			{
				var word = tp.NextString().ToLowerInvariant();
				if (word != "square" && word != "round" && word != "reinforced")
					yield return $"Error: {Quote(word)} is not a tube kind. Expected square, round or reinforced."
						+ " " + ConveyorUsage;
				family = word;
			}

			if (!tp.End) yield return "Error: too many arguments. " + ConveyorUsage;

			// Opposite openings make a straight tube, perpendicular ones a curved tube. There is
			// nothing else to choose from: junctions and T-pieces are not handled yet.
			bool curved = port2 != Base6Directions.GetFlippedDirection(port1);

			var size = selectedGrid.GridSizeEnum;

			TubeVariant tube = default(TubeVariant);
			bool known = false;
			for (int i = 0; i < TubeVariants.Length; ++i)
			{
				if (TubeVariants[i].Family != family) continue;
				if (TubeVariants[i].Size != size) continue;
				if (TubeVariants[i].Curved != curved) continue;
				tube = TubeVariants[i];
				known = true;
				break;
			}

			if (!known)
				yield return $"Error: there is no {family} {(curved ? "curved" : "straight")} conveyor tube"
					+ $" for a {size.ToString().ToLowerInvariant()} grid. Round pipes are large-grid only;"
					+ " square and reinforced tubes exist for both.";

			MyCubeBlockDefinition definition;
			var definitionId = new MyDefinitionId(typeof(MyObjectBuilder_ConveyorConnector), tube.Subtype);
			if (!MyDefinitionManager.Static.TryGetCubeBlockDefinition(definitionId, out definition))
				yield return $"Error: this world has no block {Quote(tube.Subtype)}."
					+ (family == "round" ? " Round pipes come with the Heavy Industry pack." : "");

			IMySlimBlock neighbour;
			Vector3I neighbourCell;
			var refusal = CheckBuildSite(ijk, out neighbour, out neighbourCell);
			if (refusal != null) yield return refusal;

			MyBlockOrientation orientation;
			if (!FindTubeOrientation(tube, port1, port2, out orientation))
				yield return $"Internal error: no orientation of {Quote(definition.DisplayNameText)}"
					+ $" puts its openings on {Dir(port1)} and {Dir(port2)}.";

			yield return BuildAt(definition, ijk, orientation.Forward, orientation.Up);

			yield return Success($"Placed {Quote(definition.DisplayNameText)} at {IJK(ijk)},"
				+ $" openings on {Dir(port1)} and {Dir(port2)}, touching {Quote(Name(neighbour))} at {IJK(neighbourCell)}."
				+ $" It stands at minimum integrity — weld it now: `weld {IJK(ijk)}`");
		}
	}
}
