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
		// Forward is -Z (Vector3I.cs:104-109); a naive reading flips the sign of every rotated
		// block on the Z axis.
		private static bool ParseDirection(string s, out Base6Directions.Direction dir)
		{
			dir = Base6Directions.Direction.Forward;
			if (s == null || s.Length != 2) return false;

			bool plus = s[0] == '+';
			if (!plus && s[0] != '-') return false;

			switch (char.ToUpperInvariant(s[1]))
			{
				case 'X': case 'I': dir = plus ? Base6Directions.Direction.Right    : Base6Directions.Direction.Left;    return true;
				case 'Y': case 'J': dir = plus ? Base6Directions.Direction.Up       : Base6Directions.Direction.Down;    return true;
				case 'Z': case 'K': dir = plus ? Base6Directions.Direction.Backward : Base6Directions.Direction.Forward; return true;
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

		private string CheckBuildSite(Vector3I ijk, out MyCubeBlockDefinition neighbour, out Vector3I neighbourCell)
		{
			neighbour = null;
			neighbourCell = Vector3I.Zero;

			double reach = (selectedGrid.GridIntegerToWorld(ijk) - GetEngineerCenter()).Length();
			if (reach > Constants.MaxInteractionDistance)
				return $"Error: {IJK(ijk)} is too far from you ({Distance(reach)})";

			var occupant = selectedGrid.CellDefinition(ijk);
			if (occupant != null)
				return $"Error: {IJK(ijk)} is not empty — {Quote(Name(occupant))} stands there.";

			if (selectedGrid.WorldToGridInteger(GetEngineerCenter()) == ijk)
				return $"Error: {IJK(ijk)} is where you stand — you cannot place a block on yourself.";

			foreach (var offset in Constants.SixDirections)
			{	var d = selectedGrid.CellDefinition(ijk + offset);
				if (d == null) continue;
				neighbour = d;
				neighbourCell = ijk + offset;
				return null;
			}

			return $"Error: no block touches {IJK(ijk)} by a face, so the new block would have nothing"
				+ " to hold on to. Place it against the existing structure.";
		}

		internal void SwitchCubePlacer(bool hold)
		{
			var controller = character as Sandbox.Game.Entities.IMyControllableEntity;

			// null binds to the MyToolbarItemWeapon overload; MyDefinitionId is a struct and
			// cannot express "nothing".
			if (hold) controller.SwitchToWeapon(new MyDefinitionId(typeof(MyObjectBuilder_CubePlacer)));
			else      controller.SwitchToWeapon(null);
		}

		private IEnumerator HoldCubePlacer(bool hold)
		{
			SwitchCubePlacer(hold);

			SetPause(1);
			while(IsPaused()) yield return null;
		}

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

			// AddBlock refuses occupied cells but does not check mount points, so a disconnected
			// block would hang in the air — hence the face-adjacency test in CheckBuildSite.

			if (!world.PlaceBlock(selectedGrid, ob))
				yield return $"Error: the game refused to place {Quote(definition.DisplayNameText)} at {IJK(ijk)}.";
		}

		private static string ParseFacing(ToolCall call,
			out Base6Directions.Direction forward, out Base6Directions.Direction up)
		{
			forward = Base6Directions.Direction.Forward;
			up = Base6Directions.Direction.Up;

			var fs = call.Str("facing");
			var us = call.Str("up");

			if (fs.Length == 0)
			{	if (us.Length != 0)
					return "Error: `up` only makes sense together with `facing`.";
				return null;
			}

			if (!ParseHorizDir(fs, out forward))
				return $"Error: {Quote(fs)} is not a facing direction. Expected one of forward backward left right.";

			if (us.Length != 0 && !ParseVertDir(us, out up))
				return $"Error: {Quote(us)} is not an up direction. Expected up or down.";

			return null;
		}

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

		internal IEnumerator Place(ToolCall call)
		{
			string message;
			if (!GridIsSet(out message)) yield return message;
			if (CurrentGridIsProjection(out message)) yield return message;

			var query = call.Str("type");
			if (string.IsNullOrEmpty(query))
				yield return call.Need("type");

			Vector3I ijk;
			if (!call.Ijk(out ijk))
				yield return call.NeedIjk;

			MyCubeBlockDefinition neighbour;
			Vector3I neighbourCell;
			var refusal = CheckBuildSite(ijk, out neighbour, out neighbourCell);
			if (refusal != null) yield return refusal;

			Base6Directions.Direction forward, up;
			refusal = ParseFacing(call, out forward, out up);
			if (refusal != null) yield return refusal;

			string error;
			var definition = ResolvePlaceable(query, out error);
			if (definition == null) yield return error;

			yield return HoldCubePlacer(true);
			try
			{	yield return PlaceCube(definition, ijk, forward, up);
				yield return HoldCubePlacer(false);
			}
			finally
			{	// An error above disposes the whole coroutine stack; the placer must not stay in hand.
				SwitchCubePlacer(false);
			}

			yield return Success($"Placed {Quote(definition.DisplayNameText)} at {IJK(ijk)}, touching {Quote(Name(neighbour))} at {IJK(neighbourCell)}");
		}
	}
}
