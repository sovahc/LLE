using System.Collections.Generic;
using System.Text;

using VRageMath;
using Sandbox.Game.Entities;

namespace LLE
{
	public partial class Commands
	{
		private const string OrientUsage = "Usage: orient 'Conveyor Tube' 3 2 5 ports -Y +Y";

		private const int MaxOrientationLines = 8;

		// Of the 24 cube orientations, only 8 have forward horizontal and up vertical —
		// the ones the simplified `place` format can express.
		private const int PlaceableOrientations = 8;

		private struct OrientOption
		{
			public MyBlockOrientation Orientation;
			public List<Base6Directions.Direction> Ports;
			public int Connections;
			public string ConnectsTo;
		}

		internal CommandResult Orient(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) return message;
			if (CurrentGridIsProjection(out message)) return message;

			var query = tp.NextString();
			if (string.IsNullOrEmpty(query))
				return "Error: expected a block type. " + OrientUsage;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk))
				return "Error: expected I J K after the block type. " + OrientUsage;

			bool wantPorts = false;
			var p1 = Base6Directions.Direction.Forward;
			var p2 = Base6Directions.Direction.Up;

			if (!tp.End)
			{
				if (!tp.Match("ports"))
					return "Error: after the cell, expected `ports D D₂` or nothing at all. " + OrientUsage;

				var s1 = tp.NextString();
				if (!ParseDirection(s1, out p1))
					return $"Error: {Quote(s1)} is not a direction. Expected one of +X -X +Y -Y +Z -Z. " + OrientUsage;

				var s2 = tp.NextString();
				if (!ParseDirection(s2, out p2))
					return $"Error: {Quote(s2)} is not a direction. Expected one of +X -X +Y -Y +Z -Z. " + OrientUsage;

				if (!tp.End) return "Error: too many arguments. " + OrientUsage;

				if (p1 == p2) return "Error: the two ports must look in different directions.";

				wantPorts = true;
			}

			string error;
			var definition = FindPlaceableBlock(query, out error);
			if (definition == null) return error;

			if (definition.Size != Vector3I.One)
				return $"Error: `orient` handles 1x1x1 blocks only for now, and {Quote(definition.DisplayNameText)}"
					+ $" is {definition.Size.X}x{definition.Size.Y}x{definition.Size.Z}.";

			var occupant = selectedGrid.GetCubeBlock(ijk);
			if (occupant != null)
				return $"Error: {IJK(ijk)} is not empty — {Quote(Name(occupant))} stands there. Pick a free cell.";

			bool hasPorts = ConveyorPorts.HasPorts(definition);

			if (wantPorts && !hasPorts)
				return $"Error: {Quote(definition.DisplayNameText)} has no conveyor ports, so it cannot be asked for a ports pair."
					+ $" Run `orient {Quote(definition.DisplayNameText)} {IJK(ijk)}` instead.";

			// Which faces of this cell have a neighbour offering a port back at us.
			var openTo = new List<Base6Directions.Direction>();
			var openToName = new List<string>();

			var neighbourPorts = new List<Base6Directions.Direction>();
			for (int i = 0; i < ConveyorPorts.Six.Length; ++i)
			{
				var d = ConveyorPorts.Six[i];
				var cell = ijk + Base6Directions.GetIntVector(d);

				var nb = selectedGrid.GetCubeBlock(cell);
				if (nb == null) continue;

				ConveyorPorts.AtCell(nb, cell, neighbourPorts);
				if (!ConveyorPorts.Contains(neighbourPorts, Base6Directions.GetFlippedDirection(d))) continue;

				openTo.Add(d);
				openToName.Add($"{Quote(Name(nb))} at {IJK(cell)}");
			}

			var mountPoints = definition.GetBuildProgressModelMountPoints(MyComponentStack.NewBlockIntegrity);
			var mcg = selectedGrid as MyCubeGrid;

			var attaching = new List<OrientOption>();
			var portMatchAnywhere = new List<MyBlockOrientation>(); // right ports, nothing to attach to yet
			var portSetsSeen = new List<string>();
			var achievable = new List<string>();

			foreach (var orientation in ConveyorPorts.AllOrientations)
			{
				// The simplified `place` format expresses only forward-horizontal / up-vertical
				// orientations; skip the rest so every line below is copy-pasteable.
				var of = orientation.Forward;
				if (of == Base6Directions.Direction.Up || of == Base6Directions.Direction.Down) continue;
				var ou = orientation.Up;
				if (ou != Base6Directions.Direction.Up && ou != Base6Directions.Direction.Down) continue;

				var ports = new List<Base6Directions.Direction>();
				if (hasPorts) ConveyorPorts.InOrientation(definition, orientation, ports);

				// "ports A B" asks for a block whose ports reach A and B. A junction has six and
				// satisfies any pair; a tube has exactly two and satisfies one pair.
				if (wantPorts &&
					!(ConveyorPorts.Contains(ports, p1) && ConveyorPorts.Contains(ports, p2)))
				{
					var text = PortText(ports);
					if (text.Length != 0 && !achievable.Contains(text)) achievable.Add(text);
					continue;
				}

				var rotation = orientation;
				Quaternion q;
				rotation.GetQuaternion(out q);

				var position = ijk;
				bool attaches = MyCubeGrid.CheckConnectivity(mcg, definition, mountPoints, ref q, ref position);

				if (!attaches)
				{	if (wantPorts) portMatchAnywhere.Add(orientation);
					continue;
				}

				int connections = 0;
				var connectsTo = new StringBuilder();
				for (int i = 0; i < ports.Count; ++i)
				{
					int at = openTo.IndexOf(ports[i]);
					if (at < 0) continue;
					if (connections != 0) connectsTo.Append(" and ");
					connectsTo.Append(openToName[at]);
					++connections;
				}

				// One line per distinct port set: the remaining rolls are the same conveyor.
				if (hasPorts)
				{
					var key = PortKey(ports);
					if (portSetsSeen.Contains(key)) continue;
					portSetsSeen.Add(key);
				}

				attaching.Add(new OrientOption
				{	Orientation = orientation,
					Ports = ports,
					Connections = connections,
					ConnectsTo = connectsTo.ToString()
				});
			}

			// --- the answer ------------------------------------------------------------------

			var name = Quote(definition.DisplayNameText);
			var md = new MyMarkdown();

			if (wantPorts)
			{
				if (attaching.Count != 0)
				{	md.Append($"place {name} {IJK(ijk)} {DirKeyword(attaching[0].Orientation.Forward)} {DirKeyword(attaching[0].Orientation.Up)}");
					return Success(md.Result());
				}

				if (portMatchAnywhere.Count != 0)
				{
					var o = portMatchAnywhere[0];
					md.Append($"place {name} {IJK(ijk)} {DirKeyword(o.Forward)} {DirKeyword(o.Up)}");
					md.Append($"Careful: nothing built touches {IJK(ijk)} on a face this block can mount by,"
						+ " so `place` will refuse it until the piece before it stands. Build the run in order.");
					return Success(md.Result());
				}

				var sb = new StringBuilder();
				sb.Append($"No orientation of {name} puts its ports on {Dir(p1)} and {Dir(p2)}.");
				if (achievable.Count != 0)
				{	sb.Append(" It can do: ");
					for (int i = 0; i < achievable.Count; ++i)
					{	if (i != 0) sb.Append("; ");
						sb.Append(achievable[i]);
					}
					sb.Append('.');
				}
				return sb.ToString();
			}

			if (attaching.Count == 0)
				return $"Error: no orientation of {name} attaches at {IJK(ijk)}."
					+ " The cell has to share a whole face with a block this one can mount by."
					+ $" Run `near {IJK(ijk)}` to see what is around it.";

			if (!hasPorts)
			{
				if (attaching.Count == PlaceableOrientations)
				{	md.Append($"{name} attaches by any face — it needs no orientation at {IJK(ijk)}.");
					md.Append($"place {name} {IJK(ijk)}");
					return Success(md.Result());
				}

				md.Append($"{name} at {IJK(ijk)}: {attaching.Count} of {PlaceableOrientations}"
					+ " orientations attach to the grid. Copy one of these:");

				for (int i = 0; i < attaching.Count && i < 3; ++i)
					md.Append($"place {name} {IJK(ijk)} {DirKeyword(attaching[i].Orientation.Forward)} {DirKeyword(attaching[i].Orientation.Up)}");

				return Success(md.Result());
			}

			attaching.Sort((x, y) => y.Connections.CompareTo(x.Connections));

			md.Append($"{name} at {IJK(ijk)}: {attaching.Count} distinct ways its ports can lie."
				+ " Each line below is a complete command — copy the one whose ports go where you need them.");

			for (int i = 0; i < attaching.Count && i < MaxOrientationLines; ++i)
			{
				var o = attaching[i];
				var line = new StringBuilder();
				line.Append($"place {name} {IJK(ijk)} {DirKeyword(o.Orientation.Forward)} {DirKeyword(o.Orientation.Up)}");
				line.Append("   -- ports ");
				line.Append(PortText(o.Ports));
				if (o.Connections != 0) line.Append($", connects to {o.ConnectsTo}");
				md.Append(line.ToString());
			}

			if (attaching.Count > MaxOrientationLines)
				md.Append($"({attaching.Count - MaxOrientationLines} more, none of them connecting to anything here.)");

			return Success(md.Result());
		}

		private static string PortKey(List<Base6Directions.Direction> ports)
		{
			int mask = 0;
			for (int i = 0; i < ports.Count; ++i) mask |= 1 << (int)ports[i];
			return mask.ToString();
		}

		// Canonical order, so the same set of ports always reads the same way.
		private static string PortText(List<Base6Directions.Direction> ports)
		{
			if (ports.Count == 0) return "";

			var sorted = new List<Base6Directions.Direction>(ports);
			sorted.Sort();

			var sb = new StringBuilder();
			for (int i = 0; i < sorted.Count; ++i)
			{	if (i != 0) sb.Append(' ');
				sb.Append(Dir(sorted[i]));
			}
			return sb.ToString();
		}
	}
}
