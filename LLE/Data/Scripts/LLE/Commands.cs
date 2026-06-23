using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;

namespace LLE
{
	public partial class Commands
	{
		private const string IE_NO_INVENTORY = "Internal error: character.GetInventory() is null";

		private IMyCubeGrid selectedGrid;
		private MyVoxelBase selectedAsteroid;

		private readonly IMyCharacter character;

		private Status status;

		internal string Status_ReportChanged() => status.ReportChanged();
		
		private IEnumerator currentCommand;

		private double resumeTime;

		private void SetPause(double time)
		{	resumeTime = Time.Now + time;
		}
		private bool IsPaused()
		{	return Time.Now < resumeTime;			
		}

		public Commands(IMyCharacter character_)
		{
			character = character_;
			status = new Status(character);

			foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyPhysicalItemDefinition>())
			{
				if (def.Id.TypeId == typeof(MyObjectBuilder_Component))
					ALL_COMPONENTS.Add(def.Id.SubtypeName);
			}
		}

		internal string Help()
		{
			return @"
You are an autonomous agent controlling a Space Engineer in-game character.
Your goal is to execute instructions from the chat.

## ENVIRONMENT
You are inside Space Engineers game.
You control a character that can fly, weld, grind, and manage inventories.
You operate on a selected grid (ship or station).

## EXECUTION RULES

1. First think about your next actions, then on the last line output: Execute `command`, for example: Execute `fly -10 5 3`.
2. Your tasks will be described in the chat. If you don't have a task - execute the `pause` command.
3. When you complete a task or are stuck, report status using `say 'text'`.

## HINTS

1. If you lack required components, execute `inventories` to list all containers.

## AVAILABLE COMMANDS

* select 'name'		    		- Select a ship or station on which to grind, weld, fly, and perform other operations.
* overview						- List grid blocks by category.
* integrity						- Show damaged blocks on the selected grid.
* fly I J K						- Fly to specific grid coordinates. e.g. `fly 10 -5 13`
* grind I J K					- Grind a block at specific coordinates.
* weld I J K					- Weld a block at specific coordinates.
* near							- Return 6 accessible blocks around you and the block you are standing on.
* near I J K					- Return 6 accessible blocks around a block at specific coordinates.
* inventory						- Return the items in your inventory.
* inventory I J K				- Return the inventory of the container at specific coordinates.
* inventories					- Return all inventories on the selected grid.
* get count 'item' from I J K	- Transfer an item from a container to your inventory. e.g. `get 10 'Gold Ingot' from -1 5 2`
* put count 'item' into I J K	- Transfer an item from your inventory to a container. e.g. `put 1 'Medkit' into 14 0 2`
* put all components into I J K	- Transfer all blocks components from your inventory to a container (very useful shortcut).
* transfer count 'item' from I1 J1 K1 to I2 J2 K2 - Transfer an item from one inventory to another.
* status					- Check bot status: Health, Oxygen, Hydrogen, Energy.
* say 'message'					- Send a message to the in-game chat.
";
}

/*
* select_asteroid 'name'		- Select an asteroid on which to mine.
* search 'substring'			- Search block coordinates by name.
search ['substring']   - Find any objects by partial match. Ex: `search` (search anything), `search STATION`, `search Steel Plate`
info 'name'        - Get detailed information about a specific object.
look at 'name'     - Rotate to face the object
hack 'block_name'  - Grind a specific block just below the hacking point (weld it back to restore functionality).
mine 'block_name'  - Mine a specific ore deposit.
status             - Check bot status: Health, Oxygen, Hydrogen, Energy.
pickup 'name'      - Pick up a specified object.
drop 'name' [quantity|all] - Drop a specified object.
? move {forward|backward|left|right|up|down} {distance} - Move in a direction
? recover from being stuck
? save to memory 'string'
! Pathfinding: safest (default) / shortest / scouting / prefer open space

*/

		private static bool Include(string searchTerm, string text)
		{	if(searchTerm == "" || searchTerm == "*") return true;
			return text.Contains(searchTerm);
		}

		private string MyError(Vector3D engineer, string query, List<MyEntity> matches)
		{
			if(matches.Count == 0)
				return $"Error: object '{query}' not found. Use the exact object name.";

			StringBuilder sb = new StringBuilder();
			sb.Append($"Error: multiple objects match '{query}':\n");
			foreach (var e in matches)
			{
				string category, name;
				Description(e, out category, out name);
				double distance = (e.WorldMatrix.Translation - engineer).Length();
				sb.Append($"* {category} {Quote(name)} → {Distance(distance)}\n");
			}
			sb.Append("\n\n");
			return sb.ToString();
		}

		internal string Say(TokenParser tp)
		{
			var message = tp.NextString();
			if (string.IsNullOrEmpty(message))
				return "Error: provide a message. Usage: say 'Hello world'";

			MyVisualScriptLogicProvider.SendChatMessage(
				message, character.DisplayName, character.ControllerInfo.ControllingIdentityId, "Yellow");
			return "Done";
		}

		internal string Select(TokenParser tp)
		{
			var what = tp.NextString();

			const int radius = 1000;

			var engineer = Utilities.GetEngineerCenter(character);
			
			BoundingSphereD S = new BoundingSphereD(engineer, radius);
			List<MyEntity> entities = MyEntities.GetTopMostEntitiesInSphere(ref S);

			List<MyEntity> matches = new List<MyEntity>();

			string category, name;
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				Description(e, out category, out name);

				if(Include(what, name) || Include(what, category)) matches.Add(e);
			}

			if(matches.Count != 1) return MyError(engineer, what, matches);

			var select = matches[0];

			Description(select, out category, out name);

			var grid = select as IMyCubeGrid;
			if(grid != null)
			{	Debug.Start(grid);
				selectedGrid = grid;
				selectedAsteroid = null;
				return $"Selected {category} {Quote(name)}";
			}

			var asteroid = select as MyVoxelBase;
			if(asteroid != null)
			{	selectedGrid = null;
				selectedAsteroid = asteroid;
				return $"Selected {category} {Quote(name)}";
			}
			
			return $"Error: can't select {category} '{name}'";
		}

		internal bool GridIsSet(out string message)
		{	if(selectedGrid == null)
			{	message = "Error: you should select a grid first. Use `select name`";
				return false;
			}
			message = null;
			return true;
		}

		internal void AppendNearbyFreeCells(Vector3I ijk, StringBuilder sb)
		{	
			Vector3D ec = Utilities.GetEngineerCenter(character);

			var minimalDistanceSq = double.MaxValue;
			var nearestFreeSpace = Vector3I.Zero;

			sb.Append("(");

			int added = 0;
			foreach (var direction in Constants.SixDirections)
			{	var position = ijk + direction;
					
				var block = selectedGrid.GetCubeBlock(position);
				if(Collisions.CenterIsFree(block, position))
				{	if(added != 0) sb.Append("; ");
					sb.Append(IJK(position));
					++added;

					var bp = selectedGrid.GridIntegerToWorld(position);
					var dsq = (ec - bp).LengthSquared();

					if(dsq < minimalDistanceSq)
					{	minimalDistanceSq = dsq;
						nearestFreeSpace = position;
					}
				}
			}
			if(added == 0)
			{	sb.Append(" -- none -- ");
			}

			sb.Append(")\n");

			if(added > 1)
			{	sb.Append($"(Nearest to you is {IJK(nearestFreeSpace)})");
			}
		}

		internal bool IsTooFar(Vector3I ijk, out string message)
		{
			var block = selectedGrid.GetCubeBlock(ijk);

			Vector3D world;
			block.ComputeWorldCenter(out world);
			var distance = (world - Utilities.GetEngineerCenter(character)).Length();
			if(distance > 6) // XX 5->6 for Large container
			{	
				StringBuilder sb = new StringBuilder();
				sb.Append($"You are too far from {Name(block)} to interact ({Distance(distance)})\n");
				sb.Append($"Possible interaction points is: ");
					AppendNearbyFreeCells(ijk, sb);
				message = sb.ToString();
				return true;
			}
			message = null;
			return false;
		}

		internal bool InProgress()
		{	return currentCommand != null;
		}

		internal string Update()
		{
			status.Tick();

			if (currentCommand == null) return null;

			// yield return null; = wait
			// yield retrurn string; = response to LLM
			// yield break; = no respone, done
			// ! Don't carry references to engine objects across `yield return null` that can be re-found.

			if (currentCommand.MoveNext())
			{	
				var result = currentCommand.Current as string;

				if(result != null)
				{	(currentCommand as IDisposable)?.Dispose();
					currentCommand = null;
					return result;
				}
			}
			else
			{	MyConsole.Add("!yield break!", Color.DarkRed);
				(currentCommand as IDisposable)?.Dispose();
				currentCommand = null;
			}
			return null;
		}

		internal string Execute(string command)
		{
			//Utilities.Log($"Execute `{command}`");

			string result = null;

			var tp = new TokenParser(command);

			if(tp.Match("Overview"))
			{	result = Overview();
			}
			else if(tp.Match("Integrity"))
			{	result = Integrity();
			}
			else if(tp.Match("Select"))
			{	result = Select(tp);
			}
			else if(tp.Match("Fly"))
			{	currentCommand = Fly(tp);
			}
			else if(tp.Match("Grind"))
			{	currentCommand = Grind(tp);
			}
			else if(tp.Match("Weld"))
			{	currentCommand = Weld(tp);
			}
			else if(tp.Match("Near"))
			{	result = Near(tp);
			}
			else if(tp.Match("Inventory"))
			{	result = Inventory(tp);
			}
			else if(tp.Match("Inventories"))
			{	result = Inventories();
			}
			else if(tp.Match("Get"))
			{	currentCommand = Get(tp);
			}
			else if(tp.Match("Put"))
			{	currentCommand = Put(tp);
			}
			else if(tp.Match("Status"))
			{	result = status.ReportAll();
			}
			else if(tp.Match("Say"))
			{	result = Say(tp);
			}
			else if(tp.Match("Transfer"))
			{	currentCommand = Transfer(tp);
			}
			else
			{	result = $"Unknown command '{tp.NextString()}'.";
			}

			return result;
		}
	}
}
