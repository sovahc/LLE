using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ObjectBuilders;
using Sandbox.Definitions;
using Sandbox.Game.Entities;

namespace LLE
{
	public partial class Commands
	{
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

		private static bool ParseHorizDir(string s, out Base6Directions.Direction dir)
		{
			dir = Base6Directions.Direction.Forward;
			if (s == null) return false;
			switch (s.ToLowerInvariant())
			{
				case "forward":  dir = Base6Directions.Direction.Forward;  return true;
				case "backward": dir = Base6Directions.Direction.Backward; return true;
				case "left":     dir = Base6Directions.Direction.Left;     return true;
				case "right":    dir = Base6Directions.Direction.Right;    return true;
			}
			return false;
		}

		private static bool ParseVertDir(string s, out Base6Directions.Direction dir)
		{
			dir = Base6Directions.Direction.Up;
			if (s == null) return false;
			switch (s.ToLowerInvariant())
			{
				case "up":   dir = Base6Directions.Direction.Up;   return true;
				case "down": dir = Base6Directions.Direction.Down; return true;
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
			{	error = $"Error: no block type matches {Quote(query)} on this grid. Use the exact name, e.g. `place 'Light Armor Block' at 3 2 4`.";
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

		// Shared by `place` and `place conveyor`: the cell has to be within the engineer's reach,
		// empty, and touched by a face of something already built. Returns the refusal, or null.
		private string CheckBuildSite(Vector3I ijk, out IMySlimBlock neighbour, out Vector3I neighbourCell)
		{
			neighbour = null;
			neighbourCell = Vector3I.Zero;

			// The engineer builds with his hands, not across the map.
			double reach = (selectedGrid.GridIntegerToWorld(ijk) - GetEngineerCenter()).Length();
			if (reach > Constants.MaxInteractionDistance)
				return $"Error: {IJK(ijk)} is too far from you ({Distance(reach)})";

			var occupant = selectedGrid.GetCubeBlock(ijk);
			if (occupant != null)
				return $"Error: {IJK(ijk)} is not empty — {Quote(Name(occupant))} stands there.";

			// A block built in the engineer's own cell would embed him in the wall.
			if (selectedGrid.WorldToGridInteger(GetEngineerCenter()) == ijk)
				return $"Error: {IJK(ijk)} is where you stand — you cannot place a block on yourself.";

			foreach (var offset in Constants.SixDirections)
			{	var b = selectedGrid.GetCubeBlock(ijk + offset);
				if (b == null) continue;
				neighbour = b;
				neighbourCell = ijk + offset;
				return null;
			}

			return $"Error: no block touches {IJK(ijk)} by a face, so the new block would have nothing"
				+ " to hold on to. Place it against the existing structure.";
		}

		// Taking the cube placer out and putting it away is a second of animation each way, so
		// it is separate from the placement: `build` pays for it once per draft, not per block.
		private IEnumerator HoldCubePlacer(bool hold)
		{
			var controller = character as Sandbox.Game.Entities.IMyControllableEntity;

			// SwitchToWeapon(null) binds to the MyToolbarItemWeapon overload — that is how the
			// tool is put away; MyDefinitionId is a struct and cannot express "nothing".
			if (hold) controller.SwitchToWeapon(new MyDefinitionId(typeof(MyObjectBuilder_CubePlacer)));
			else      controller.SwitchToWeapon(null);

			SetPause(1);
			while(IsPaused()) yield return null;
		}

		// The placement itself: turn to the cell and put the block down at minimum integrity.
		// The cube placer must already be in hand. Yields an error string if the game refuses,
		// and ends silently on success — the caller words the answer.
		private IEnumerator PlaceCube(MyCubeBlockDefinition definition, Vector3I ijk,
			Base6Directions.Direction forward, Base6Directions.Direction up)
		{
			var ob = MyObjectBuilderSerializer.CreateNewObject(definition.Id) as MyObjectBuilder_CubeBlock;
			if (ob == null)
				yield return $"Internal error: no object builder for {Quote(definition.DisplayNameText)}";

			var target = selectedGrid.GridIntegerToWorld(ijk);

			SetPause(Constants.MicronavigationDelay);
			while(IsPaused())
			{
				CharacterRotateTo(target);
				yield return null;
			}

			ob.EntityId = 0;
			ob.Min = ijk;
			ob.BlockOrientation = new SerializableBlockOrientation(forward, up);
			ob.Owner = character.ControllerInfo.ControllingIdentityId;
			ob.BuiltBy = ob.Owner;

			ob.IntegrityPercent = MyComponentStack.MOUNT_THRESHOLD;
			ob.BuildPercent = MyComponentStack.MOUNT_THRESHOLD;

			// AddBlock refuses occupied cells by itself, but it does not check mount points, so a
			// disconnected block would just hang in the air — hence the face-adjacency test in
			// CheckBuildSite. Which face the block mounts by is left to the model on purpose: that
			// is the thing we are here to watch.

			var placed = selectedGrid.AddBlock(ob, false);
			if (placed == null)
				yield return $"Error: the game refused to place {Quote(definition.DisplayNameText)} at {IJK(ijk)}.";
		}

		// `facing forward|backward|left|right [up|down]` — the optional tail `place` and `draft`
		// share. Returns the refusal, or null.
		private static string ParseFacing(TokenParser tp, string usage,
			out Base6Directions.Direction forward, out Base6Directions.Direction up)
		{
			forward = Base6Directions.Direction.Forward;
			up = Base6Directions.Direction.Up;

			if (tp.End) return null;

			if (!tp.Match("facing"))
				return "Error: expected `facing` before the side direction. " + usage;

			var fs = tp.NextString();
			if (!ParseHorizDir(fs, out forward))
				return $"Error: {Quote(fs)} is not a facing direction. Expected one of forward backward left right. " + usage;

			if (!tp.End)
			{
				var us = tp.NextString();
				if (!ParseVertDir(us, out up))
					return $"Error: {Quote(us)} is not an up direction. Expected up or down. " + usage;
			}

			if (!tp.End)
				return "Error: too many arguments. " + usage;

			return null;
		}

		// FindPlaceableBlock plus the 1x1x1 restriction, shared by `place` and `draft`.
		private MyCubeBlockDefinition ResolvePlaceable(string query, out string error)
		{
			var definition = FindPlaceableBlock(query, out error);
			if (definition == null) return null;

			if (definition.Size != Vector3I.One)
			{	error = $"Error: only 1x1x1 blocks can be placed for now, and {Quote(definition.DisplayNameText)}"
					+ $" is {definition.Size.X}x{definition.Size.Y}x{definition.Size.Z}.";
				return null;
			}

			return definition;
		}

		internal IEnumerator Place(TokenParser tp)
		{
			const string usage = "Usage: place 'Block Name' at I J K [facing forward|backward|left|right] [up|down]";

			string message;
			if (!GridIsSet(out message)) yield return message;
			if (CurrentGridIsProjection(out message)) yield return message;

			var query = tp.NextString();
			if (string.IsNullOrEmpty(query))
				yield return "Error: expected a block type. " + usage;

			if (!tp.Match("at"))
				yield return "Error: expected `at` before the coordinates. " + usage;

			Vector3I ijk;
			if (!tp.NextVector3I(out ijk))
				yield return "Error: expected I J K after `at`. " + usage;

			IMySlimBlock neighbour;
			Vector3I neighbourCell;
			var refusal = CheckBuildSite(ijk, out neighbour, out neighbourCell);
			if (refusal != null) yield return refusal;

			Base6Directions.Direction forward, up;
			refusal = ParseFacing(tp, usage, out forward, out up);
			if (refusal != null) yield return refusal;

			string error;
			var definition = ResolvePlaceable(query, out error);
			if (definition == null) yield return error;

			yield return HoldCubePlacer(true);
			yield return PlaceCube(definition, ijk, forward, up);
			yield return HoldCubePlacer(false);

			yield return Success($"Placed {Quote(definition.DisplayNameText)} at {IJK(ijk)}, touching {Quote(Name(neighbour))} at {IJK(neighbourCell)}");
		}
	}
}
