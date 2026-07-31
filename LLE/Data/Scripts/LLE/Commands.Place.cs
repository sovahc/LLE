using System;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;

// AddBlock refuses occupied cells by itself, but it does not check mount points, so a
// disconnected block would just hang in the air — hence the face-adjacency test below.
// Which face the block mounts by is left to the model on purpose: that is the thing we
// are here to watch.

namespace LLE
{
	public partial class Commands
	{
		private static readonly Vector3I[] FaceNeighbours =
		{	Vector3I.Right, Vector3I.Left,
			Vector3I.Up, Vector3I.Down,
			Vector3I.Backward, Vector3I.Forward
		};

		private const string PlaceUsage = "Usage: place 'Gyroscope' 3 2 4 facing +X up +Y";

		// `+X` … `-Z` as the LLM writes them. Vector3I.cs:104-109 — note that Forward is -Z,
		// so a naive reading flips the sign of every rotated block on the Z axis.
		private static bool ParseDirection(string s, out Base6Directions.Direction dir)
		{
			dir = Base6Directions.Direction.Forward;
			if (s == null || s.Length != 2) return false;

			bool plus = s[0] == '+';
			if (!plus && s[0] != '-') return false;

			switch (char.ToUpperInvariant(s[1]))
			{
				case 'X': dir = plus ? Base6Directions.Direction.Right    : Base6Directions.Direction.Left;    return true;
				case 'Y': dir = plus ? Base6Directions.Direction.Up       : Base6Directions.Direction.Down;    return true;
				case 'Z': dir = plus ? Base6Directions.Direction.Backward : Base6Directions.Direction.Forward; return true;
			}
			return false;
		}

		private MyCubeBlockDefinition FindPlaceableBlock(string query, out string error)
		{
			error = null;

			var partial = new List<MyCubeBlockDefinition>();

			foreach (var d in MyDefinitionManager.Static.GetAllDefinitions())
			{
				var def = d as MyCubeBlockDefinition;
				if (def == null || !def.Public) continue;
				if (def.CubeSize != selectedGrid.GridSizeEnum) continue;

				var name = def.DisplayNameText;
				if (string.IsNullOrEmpty(name)) continue;

				// Exact wins outright: 'Conveyor Tube' is also a substring of 'Curved Conveyor Tube'.
				if (string.Equals(name, query, StringComparison.OrdinalIgnoreCase))
					return def;

				if (name.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
					partial.Add(def);
			}

			if (partial.Count == 1) return partial[0];

			if (partial.Count == 0)
			{	error = $"Error: no block type matches {Quote(query)} on this grid. Use the exact name, e.g. `place 'Light Armor Block' 3 2 4`.";
				return null;
			}

			var sb = new StringBuilder();
			sb.Append($"Error: {Quote(query)} matches several block types:\n");
			for (int i = 0; i < partial.Count && i < 8; ++i)
				sb.Append($"* {Quote(partial[i].DisplayNameText)}\n");
			if (partial.Count > 8) sb.Append($"... and {partial.Count - 8} more\n");
			error = sb.ToString();
			return null;
		}

		internal CommandResult Place(TokenParser tp)
		{
			string message;
			if (!GridIsSet(out message)) return message;
			if (CurrentGridIsProjection(out message)) return message;

			var query = tp.NextString();
			if (string.IsNullOrEmpty(query))
				return "Error: expected a block type. " + PlaceUsage;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk))
				return "Error: expected I J K after the block type. " + PlaceUsage;

			// The engineer builds with his hands, not across the map.
			double reach = (selectedGrid.GridIntegerToWorld(ijk) - GetEngineerCenter()).Length();
			if (reach > Constants.MaxInteractionDistance)
				return $"Error: {IJK(ijk)} is {Distance(reach)} away and out of reach ({Constants.MaxInteractionDistance}m). Fly to a free cell beside it first — not into it.";

			var forward = Base6Directions.Direction.Forward;
			var up = Base6Directions.Direction.Up;

			if (!tp.End)
			{
				// Keywords optional: on long answers Gemma emits a bare `+Y +X` in argument
				// order about a third of the time. Measured in the GemmaBuilder project.
				tp.Match("facing");
				var fs = tp.NextString();
				if (!ParseDirection(fs, out forward))
					return $"Error: {Quote(fs)} is not a direction. Expected one of +X -X +Y -Y +Z -Z. " + PlaceUsage;

				tp.Match("up");
				var us = tp.NextString();
				if (!ParseDirection(us, out up))
					return $"Error: {Quote(us)} is not a direction. Expected one of +X -X +Y -Y +Z -Z. " + PlaceUsage;

				if (char.ToUpperInvariant(fs[1]) == char.ToUpperInvariant(us[1]))
					return $"Error: facing {fs} and up {us} are on the same axis. They must be perpendicular.";

				if (!tp.End)
					return "Error: too many arguments. " + PlaceUsage;
			}

			string error;
			var definition = FindPlaceableBlock(query, out error);
			if (definition == null) return error;

			if (definition.Size != Vector3I.One)
				return $"Error: `place` handles 1x1x1 blocks only for now, and {Quote(definition.DisplayNameText)} is {definition.Size.X}x{definition.Size.Y}x{definition.Size.Z}.";

			var occupant = selectedGrid.GetCubeBlock(ijk);
			if (occupant != null)
				return $"Error: {IJK(ijk)} is not empty — {Quote(Name(occupant))} stands there. Pick a free cell.";

			IMySlimBlock neighbour = null;
			Vector3I neighbourCell = Vector3I.Zero;

			foreach (var offset in FaceNeighbours)
			{	var b = selectedGrid.GetCubeBlock(ijk + offset);
				if (b == null) continue;
				neighbour = b;
				neighbourCell = ijk + offset;
				break;
			}

			if (neighbour == null)
				return $"Error: no block touches {IJK(ijk)} by a face, so the new block would have nothing to hold on to. Place it against the existing structure.";

			var ob = MyObjectBuilderSerializer.CreateNewObject(definition.Id) as MyObjectBuilder_CubeBlock;
			if (ob == null)
				return $"Internal error: no object builder for {Quote(definition.DisplayNameText)}";

			ob.EntityId = 0;
			ob.Min = ijk;
			ob.BlockOrientation = new SerializableBlockOrientation(forward, up);
			ob.Owner = character.ControllerInfo.ControllingIdentityId;
			ob.BuiltBy = ob.Owner;

			ob.IntegrityPercent = MyComponentStack.MOUNT_THRESHOLD;
			ob.BuildPercent = MyComponentStack.MOUNT_THRESHOLD;

			var placed = selectedGrid.AddBlock(ob, false);
			if (placed == null)
				return $"Error: the game refused to place {Quote(definition.DisplayNameText)} at {IJK(ijk)}.";

			return Success($"Placed {Quote(definition.DisplayNameText)} at {IJK(ijk)}, touching {Quote(Name(neighbour))} at {IJK(neighbourCell)}."
				+ $" It stands at minimum integrity — weld it now: `weld {IJK(ijk)}`");
		}
	}
}
