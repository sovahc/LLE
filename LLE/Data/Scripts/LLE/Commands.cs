using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;

// Stack-based coroutine runner:
//   yield return null;         = wait one tick
//   yield return Success(msg)  = final success response to LLM (terminates whole command)
//   yield return "error msg"   = final error response to LLM (terminates whole command)
//   yield return IEnumerator;  = run nested coroutine to completion, then resume parent
//   yield break;              = done at this level (parent resumes, or command ends)
// ! Re-query engine objects after `yield return null;` don't cache references.
//
// Design note: `yield return "error msg"` without a trailing `yield break;` works because
// Commands.Update() disposes the entire coroutine stack the moment it receives a string.
// The code after such a yield never executes — this is intentional: it avoids a redundant
// `yield break;` after every error path, keeping the coroutine bodies compact.

namespace LLE
{
	public partial class Commands
	{
		private const string IE_NO_INVENTORY = "Internal error: character.GetInventory() is null";

		internal static CommandResult Success(string message) => CommandResult.Success(message);
		internal static CommandResult Incomplete(string message) => CommandResult.Incomplete(message);

		private static readonly MyDefinitionId hydrogenId =
			new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Hydrogen");
		private static readonly MyDefinitionId electricityId =
			new MyDefinitionId(typeof(MyObjectBuilder_GasProperties), "Electricity");

		private IMyCubeGrid selectedGrid;
		private MyVoxelBase selectedAsteroid;

		private readonly IMyCharacter character;

		private Status status;

		internal string Status_ReportChanged() => status.ReportChanged();
		internal void Status_Tick() => status.Tick();
		
		private readonly Stack<IEnumerator> coroutineStack = new Stack<IEnumerator>();

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

			ALL_COMPONENTS.Clear();
			foreach (var def in MyDefinitionManager.Static.GetDefinitionsOfType<MyPhysicalItemDefinition>())
			{
				if (def.Id.TypeId == typeof(MyObjectBuilder_Component))
					ALL_COMPONENTS.Add(def.Id.SubtypeName);
			}
		}

		public Vector3D GetEngineerCenter()
		{
			return character.GetPosition() + Constants.EngineerHeight/2 * character.WorldMatrix.Up;
		}

		internal static string Help()
		{
			return @"
You are an autonomous agent controlling a Space Engineer in-game character.
Your goal is to execute instructions from the chat.

## ENVIRONMENT
You are inside the Space Engineers game.
You control a character that can fly, weld, grind, and manage inventories.
You operate on a selected grid (ship or station).

## EXECUTION RULES

1. First, think about your next actions. At the end of your response, output commands on consecutive lines starting with: Execute `command`. All trailing lines starting with Execute will be executed in order.
2. Your tasks will be described in the [GAME CHAT]. If you don't have a task, use the `pause` command.
3. Only report results after completing a task using `say 'text'`. Do not send progress updates during execution.
4. If a task is complex or you hit an obstacle, use `note 'text'` to record your intent or how you'll adapt — it carries forward to your next step.
5. Do not execute more than three commands per turn.

## HINTS

1. If you are missing required components or tools, run `inventories` on all grids to list all containers.

## AVAILABLE COMMANDS

* pause
  Puts the bot on pause. (If an event occurs in the world, the pause is automatically lifted)

* select 'name'
  Select a large ship or station on which to grind, weld, fly, and perform other operations.

* overview
  List grid blocks by category.

* integrity
  Show damaged blocks on the selected grid.

* fly I J K
  Fly to specific grid coordinates. If block coordinates are specified instead of free space, the bot flies to the interaction point with the block.

* grind I J K
  Grind a block at specific coordinates.

* weld I J K
  Weld a block at specific coordinates.

* near
  Return all blocks in 3x3x3 cube around you (27 positions).

* near I J K
  Return all blocks in 3x3x3 cube around specified coordinates.

* slice Xmin Xmax Ymin Ymax Zmin Zmax
  Return blocks in a 2D table. One axis must be length 1 (min=max), max slice size 10x10.

* inventory
  Return the items in your inventory.

* inventory I J K
  Return the inventory of the container at specific coordinates.

* inventories
  Return all inventories on the selected grid.

* get N 'item' from I J K
  Transfer N items from a container to your inventory, e.g. `get 10 'Gold Ingot' from -1 5 2`

* put N 'item' into I J K
  Transfer N items from your inventory to a container, e.g. `put 1 'Medkit' into 14 0 2`

* put all components into I J K
  Transfer all block components from your inventory to a container (very useful shortcut).

* transfer N 'item' from I J K to I₂ J₂ K₂
  Transfer N items from one inventory to another.

* search item 'substring' [N]
  Find items across nearby grids by partial name match. Returns N closest results (default 5).
  Example: search item 'Welder'

* search block 'substring' [N]
  Find blocks across nearby grids by partial name match. Returns N closest results (default 5).
  Example: search block 'Assembler' 1

* distance I J K
  Distance from you to the block at the given grid coordinates.

* points I J K
  List all interaction points for the block at the given grid coordinates.

* distance I J K I₂ J₂ K₂
  Distance between two grid coordinates (measuring tape).

* status
  Check bot vitals: Health, Oxygen, Hydrogen, Energy.

* say 'message'
  Send a message to the in-game chat.

* note 'text'
  Leave a note to yourself in the conversation. Carries forward your plan across multiple steps.
  Example: note 'base has no iron — grind 'old rover', then build reactor'

* enter I J K
  Enter the cockpit or seat at the given grid coordinates. Use `fly I J K` first to get close enough.

* exit
  Leave the current cockpit or seat.

* recharge
  List blocks on the selected grid that can recharge you.

* recharge I J K
  Recharge at the block at the given grid coordinates. Use `fly I J K` first to get close enough.
  For cockpits, cryo-chambers and seats: sits in it, exits automatically when done.
  For medblocks and survival kits: collects energy near the block through a port.
";
}
// 5. Do not work with multiple objects at once; the character's inventory is limited, so it is better to perform tasks sequentially.

/*
* power — state of the grid's power system: reactors/batteries/solar/wind, total output, battery charge

* where — where I am now: position in the world, which grid (or "in open space"), current grid cell.
Radio subsystem (vision like)
* approach I J K — fly close to coordinates, but without A* into the grid
? help recharge  -> More detailed help for a specific command.
? wait N  -> Wait N seconds.
? sound 'name'  -> play sound

? scan Scan the visible sector and return contents?
? select nearest grid

? missing I J K — show missing components for a specific block
* craft - assembler management
. produce N 'item' at I J K — enqueue in assembler
* refiner needs control over item order in inventory.
* rename I J K 'name'
* on I J K
* off I J K
? toggle I J K
? set I J K 'property' 'value' — set TerminalProperty (rotor angle, light color, limit)
? mark I J K 'label'  -> e.g. mark 10 0 -2 'main cargo'

? take all 'item' from I J K
? put all 'item' into I J K

? dump components into I J K
? dump ores into I J K
? dump ingots into I J K

* move forward 5
* go 'Assembler' && go 'Cargo' = search block 'Assembler' 1 && fly I J K
* open I J K  -> Open door
* close I J K  -> Close door

* press I J K [buttonIndex] - Press a button on a Button Panel.

* info 'name'
  Get detailed information about a specific object.
look at 'name'
  Rotate to face an object
symmetric command returning - "what am I looking at"

hack 'block_name'
  Grind a specific block just below the hacking point (weld it back to restore functionality).
mine 'ore_name'
  Mine a specific ore deposit.
pickup 'name'
  Pick up a specified object.
drop [quantity|all] 'name'
  Drop a specified object.
? move {forward|backward|left|right|up|down} {distance} - Move in a direction
? unstuck / recover - Recover from being stuck.

remember 'key' 'value' — store a key-value pair (in the mod's static Dictionary<string,string>).
recall 'key' — read.
forget 'key' — delete.
notes — print all keys.

! Pathfinding: safest (default) / shortest / scouting / prefer open space
? log - Return history of executed commands and their results
? place 'block_type' I J K [orientation]
*/
		private MyEntity3DSoundEmitter soundEmitter;

		private void PlaySound(string sound)
		{
			if (soundEmitter == null)
			{
				soundEmitter = new MyEntity3DSoundEmitter(character as MyEntity);
			}
			if (soundEmitter != null)
			{
				soundEmitter.PlaySound(new MySoundPair(sound));
			}
		}

		private void StopSound()
		{	if (soundEmitter == null) return;
			soundEmitter.StopSound(false);
		}

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

		internal CommandResult Say(TokenParser tp)
		{
			var message = tp.NextString();
			if (string.IsNullOrEmpty(message))
				return "Error: provide a message. Usage: say 'Hello world'";

			MyVisualScriptLogicProvider.SendChatMessage(
				message, character.DisplayName, character.ControllerInfo.ControllingIdentityId, "Yellow");
			return Success("Done");
		}

		internal CommandResult Note(TokenParser tp)
		{
			var text = tp.NextString();
			if (string.IsNullOrEmpty(text))
				return "Error: provide a note. Usage: note 'weld 3 0 2, then check integrity'";
			return Success("Noted.");
		}

		internal CommandResult Select(TokenParser tp)
		{
			var what = tp.NextString();

			var engineer = GetEngineerCenter();
			
			BoundingSphereD S = new BoundingSphereD(engineer, Constants.NearInformationRadius);
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
			{	
				if(grid.GridSizeEnum == MyCubeSize.Small)
					return $"Operations on small grids is not supported yet.";
				
				Debug.Start(grid);
				selectedGrid = grid;
				selectedAsteroid = null;
				return Success($"Selected {category} {Quote(name)}");
			}

			var asteroid = select as MyVoxelBase;
			if(asteroid != null)
			{	selectedGrid = null;
				selectedAsteroid = asteroid;
				return Success($"Selected {category} {Quote(name)}");
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

		internal Vector3I NearestToEngineer(List<Vector3I> list)
		{	Vector3D ec = GetEngineerCenter();

			var minimalDistanceSq = double.MaxValue;
			var nearest = Vector3I.Zero;

			foreach (var ijk in list)
			{	var world = selectedGrid.GridIntegerToWorld(ijk);
				var dsq = (ec - world).LengthSquared();

				if(dsq < minimalDistanceSq)
				{	minimalDistanceSq = dsq;
					nearest = ijk;
				}
			}
			return nearest;
		}

		internal void AppendList(List<Vector3I> list, StringBuilder sb)
		{	
			sb.Append("(");
			int added = 0;

			foreach (var ijk in list)
			{	var block = selectedGrid.GetCubeBlock(ijk);
				
				if(added != 0) sb.Append("; ");
				sb.Append(IJK(ijk));
				++added;
			}

			sb.Append(")");

			if(added >= 2)
			{	sb.Append($" (Nearest is {IJK(NearestToEngineer(list))})");
			}

			sb.Append("\n");
		}

		internal void AppendInteractionPoints(Vector3I ijk, StringBuilder sb)
		{	
			var block = selectedGrid.GetCubeBlock(ijk);

			var inventoryIP = new List<Vector3I>();
			var medblockIP = new List<Vector3I>();
			Collisions.CalculateInteractionPoints(block, inventoryIP, medblockIP);
			var grindWeldIP = new List<Vector3I>();
			Collisions.CalculateGrindWeldPoints(block, grindWeldIP);

			sb.Append("Possible interaction points are:\n");

			if(inventoryIP.Count != 0)
			{	sb.Append("* Get/Put: ");
				AppendList(inventoryIP, sb);
			}
			if(medblockIP.Count != 0)
			{	sb.Append("* Recharge: ");
				AppendList(medblockIP, sb);
			}
			if(grindWeldIP.Count != 0)
			{	sb.Append("* Grind/Weld: ");
				AppendList(grindWeldIP, sb);
			}

			if(inventoryIP.Count == 0 && medblockIP.Count == 0 && grindWeldIP.Count == 0)
			{	sb.Append("(none)\n");
			}
		}

		internal bool GetBestInteractionPoint(Vector3I ijk, out Vector3I bestIP)
		{	
			var block = selectedGrid.GetCubeBlock(ijk);

			var inventoryIP = new List<Vector3I>();
			var medblockIP = new List<Vector3I>();
			Collisions.CalculateInteractionPoints(block, inventoryIP, medblockIP);
			var grindWeldIP = new List<Vector3I>();
			Collisions.CalculateGrindWeldPoints(block, grindWeldIP);

			if(medblockIP.Count != 0)
			{	bestIP = NearestToEngineer(medblockIP);
				return true;
			}
			if(inventoryIP.Count != 0)
			{	bestIP = NearestToEngineer(inventoryIP);
				return true;
			}
			if(grindWeldIP.Count != 0)
			{	bestIP = NearestToEngineer(grindWeldIP);
				return true;
			}
			bestIP = Vector3I.Zero;
			return false;
		}

		internal bool IsAtInventoryPoint(IMySlimBlock block, out string message)
		{
			var ip = new List<Vector3I>();
			var dummy = new List<Vector3I>();
			Collisions.CalculateInteractionPoints(block, ip, dummy);
			return IsAtPoint(block, ip, out message);
		}

		internal bool IsAtMedblockPoint(IMySlimBlock block, out string message)
		{
			var ip = new List<Vector3I>();
			var dummy = new List<Vector3I>();
			Collisions.CalculateInteractionPoints(block, dummy, ip);
			return IsAtPoint(block, ip, out message);
		}

		internal bool IsAtGrindWeldPoint(IMySlimBlock block, out string message)
		{
			var ip = new List<Vector3I>();
			Collisions.CalculateGrindWeldPoints(block, ip);
			return IsAtPoint(block, ip, out message);
		}

		internal bool IsAtPoint(IMySlimBlock block, List<Vector3I> ip, out string message)
		{
			var engineerCell = selectedGrid.WorldToGridInteger(GetEngineerCenter());

			if(ip.Contains(engineerCell))
			{	message = null;
				return true;
			}

			message = "Error: You are not at the correct interaction point with the block.";
			return false;
		}

		internal bool InProgress()
		{	return coroutineStack.Count > 0;
		}

		internal CommandResult Update()
		{
			if (coroutineStack.Count == 0) return null;

			var top = coroutineStack.Peek();

			if (top.MoveNext())
			{
				var current = top.Current;

				// Final response to LLM: explicit CommandResult, or plain string (= error).
				var result = current as CommandResult;
				if(result == null)
				{	var s = current as string;
					if(s != null) result = s; // implicit: string → error
				}

				if(result != null)
				{ 
					// Dispose all
					foreach(var c in coroutineStack) (c as IDisposable)?.Dispose();
					coroutineStack.Clear();

					return result;
				}

				// Nested coroutine — push onto stack, run next tick.
				var nested = current as IEnumerator;
				if(nested != null)
					coroutineStack.Push(nested);
			}
			else
			{
				(top as IDisposable)?.Dispose();

				coroutineStack.Pop(); // parent resumes next tick, or command ends

				if(coroutineStack.Count == 0)
					MyConsole.Add("!yield break!", Color.DarkRed);
			}
			return null;
		}

		internal CommandResult Execute(string command)
		{
			//Utilities.Log($"Execute `{command}`");

			CommandResult result = null;

			var tp = new TokenParser(command);

			if(tp.Match("Pause"))
			{	LLM.pause = true;
				result = Success("OK");
			}
			else if(tp.Match("Overview"))
			{	result = Overview();
			}
			else if(tp.Match("Integrity"))
			{	result = Integrity();
			}
			else if(tp.Match("Select"))
			{	result = Select(tp);
			}
			else if(tp.Match("Fly"))
			{	coroutineStack.Push(Fly(tp));
			}
			else if(tp.Match("Grind"))
			{	coroutineStack.Push(Grind(tp));
			}
			else if(tp.Match("Weld"))
			{	coroutineStack.Push(Weld(tp));
			}
			else if(tp.Match("Near"))
			{	result = Near(tp);
			}
			else if(tp.Match("Slice"))
			{	result = Slice(tp);
			}
			else if(tp.Match("Inventory"))
			{	result = Inventory(tp);
			}
			else if(tp.Match("Inventories"))
			{	result = Inventories();
			}
			else if(tp.Match("Get"))
			{	coroutineStack.Push(Get(tp));
			}
			else if(tp.Match("Put"))
			{	coroutineStack.Push(Put(tp));
			}
			else if(tp.Match("Status"))
			{	result = Success(status.ReportAll());
			}
			else if(tp.Match("Say"))
			{	result = Say(tp);
			}
			else if(tp.Match("Note"))
			{	result = Note(tp);
			}
			else if(tp.Match("Transfer"))
			{	coroutineStack.Push(Transfer(tp));
			}
			else if(tp.Match("Search"))
			{	result = Search(tp);
			}
			else if(tp.Match("Distance"))
			{	result = Distance(tp);
			}
			else if(tp.Match("Points"))
			{	result = Points(tp);
			}
			else if(tp.Match("Enter"))
			{	result = Enter(tp);
			}
			else if(tp.Match("Exit"))
			{	result = Exit(tp);
			}
			else if(tp.Match("Recharge"))
			{	
				if(tp.End)
					result = GetRechargePoints(tp);
				else
					coroutineStack.Push(Recharge(tp));
			}
			else
			{	result = $"Unknown command '{tp.NextString()}'.";
			}

			return result;
		}
	}
}
