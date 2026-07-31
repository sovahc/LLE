using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using VRageMath;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.ModAPI;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using System.Linq;
using Sandbox.ModAPI;


// Stack-based coroutine runner:
//   yield return null;         = wait one tick
//   yield return Success(msg)  = final success response to LLM (terminates whole command)
//   yield return "error msg"   = final error response to LLM (terminates whole command)
//   yield return IEnumerator;  = run nested coroutine to completion, then resume parent
//   yield break;              = done at this level (parent resumes, or command ends)
// ! Re-query engine objects after `yield return null;` don't cache references.
// ! A top-level coroutine MUST end with a CommandResult (Success/Incomplete/string).
//   Falling off the end or `yield break` at top level is reported as Incomplete by Update().
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
		private const string E_BAD_POINT = "Error: You are not at the correct interaction point with the block.";

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

		private readonly Dictionary<string, string> memory = new Dictionary<string, string>();

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

		// Never add an exemplar answering with a bare <execute> block: with thinking off —
		// the shipping mode — that cost placement 77%→57% and orientation 77%→50% by
		// teaching terseness instead of method. Harmless with thinking on.
		internal static string Prompt()
		{
			return @"
You are an autonomous agent controlling a Space Engineer in-game character.
Your goal is to execute instructions from the chat.

## ENVIRONMENT
You are inside the Space Engineers game.
You control a character that can fly, weld, grind, build, and manage inventories.
You operate on a selected grid (ship or station).

Grid coordinates are written `I J K`. (I is the X axis, J is the Y axis, K is the Z axis)

## EXECUTION RULES

1. Before the <execute> block, write exactly these three lines, one short sentence each:
   Goal: what the chat asked you to do, in your own words.
   State: what the last command result told you.
   Plan: the commands you are about to run, and why in that order.
2. Write nothing else outside the <execute> block.
3. If the last command returned an error, do not run that same command again unchanged.
   Say in `Plan` what you are changing.
4. Put your commands inside an <execute> block.
   Output exactly one <execute> block. Multiple blocks are rejected and nothing is executed.
   Each command must be on its own line.

5. At most 3 commands per <execute> block.
6. Most commands, such as `grind`, `weld`, `put`, require you to be directly next to the target block.
7. Your tasks will be described in the [GAME CHAT]. If you don't have a task, use the `pause` command.
8. The game chat is very small; use very short phrases, and write only once you have completed the task or cannot complete it.
9. Save what matters most to memory on your own, without being asked.

## HINTS

1. If the `weld` command reports missing components, search for them with the `inventories` command on all grids near you.

## AVAILABLE COMMANDS

* pause
  Puts the bot on pause. (If an event occurs in the world, the pause is automatically lifted)

* memory 'key' 'value'
  Save a key-value pair to persistent memory. Survives context resets. The key and value are arbitrary. Writing again overwrites the previous value.
  Example: memory 'current_task' 'weld reactor at 5 0 2'
  Example 2: memory 'user_preference' 'The user asked to follow him at a distance of 50 meters.'

* restart
  Reset the conversation context. Memory (set via `memory`) is preserved.

* select 'name'
  Select a large ship or station on which to grind, weld, fly, and perform other operations.
  Reports which grid axis each of the grid's own directions points along, e.g. `up = +Y, forward = -Z`.

* overview
  List grid blocks by category.

* integrity
  Show damaged blocks on the selected grid.

* projection
  Build status of the selected projection preview: total/remaining/buildable counts, and the list of blocks buildable right now.

* fly I J K [grind|weld|get|put|recharge|enter] [headfirst]
  Fly to specific grid coordinates. If block coordinates are specified instead of free space, the bot flies to the interaction point with the block.
  With an intention keyword, flies directly to the nearest interaction point for that action (e.g. `fly 5 0 2 grind`).
  With `headfirst`, flies in a dive orientation (head first, body along travel direction) to reduce the physical collision profile. Use this to unstick when stuck in tight spaces.

* grind I J K
  Grind a block at specific coordinates. You must be in a cell next to the block to grind it.

* weld I J K
  Weld a block at specific coordinates. You must be in a cell next to the block to weld it.

* near
  Return all blocks in a 3x3x3 cube around you (27 positions).

* near I J K
  Return all blocks in a 3x3x3 cube around specified coordinates.

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

* transfer all items from I J K to I₂ J₂ K₂
  Transfer every item from one inventory to another.

* search item 'substring' [N]
  Find items across nearby grids by partial name match. Returns the N closest results (default 5).
  Example: search item 'Welder'

* search block 'substring' [N]
  Find blocks across nearby grids by partial name match. Returns the N closest results (default 5).
  Example: search block 'Assembler' 1

* distance I J K
  Distance from you to the block at the given grid coordinates.

* points I J K
  List all interaction points for the block at the given grid coordinates.

* distance from I J K to I₂ J₂ K₂
  Distance between two grid coordinates (measuring tape).

* status
  Check bot vitals: Health, Oxygen, Hydrogen, Energy.

* say 'message'
  Send a message to the in-game chat.

* enter I J K
  Enter the cockpit or seat at the given grid coordinates. 

* exit
  Leave the current cockpit or seat.

* recharge
  List blocks on the selected grid that can recharge you.

* recharge I J K
  Recharge at the block at the given grid coordinates.
  For cockpits, cryo-chambers and seats: the bot sits in it and exits automatically when done.
  For medblocks and survival kits: collects energy near the block through a port.

* route from I J K to I₂ J₂ K₂
  Compute the shortest conveyor path between the given points.
  Conveyor-capable blocks at I J K and at I₂ J₂ K₂ must already exist.
  Returns the conveyor block types that need to be built.

## TYPICAL WORKFLOWS
### Get items from cargo:
fly 5 3 1 get
get 10 'Steel Plate' from 5 3 1

### Weld a damaged block:
fly 5 0 2 weld
weld 5 0 2

### Recharge:
fly -4 3 5 recharge
recharge -4 3 5
";
}
// 5. Disabled: weak LLMs ignore this rule. Left for testing with stronger models.
//    Do not work with multiple objects at once; the character's inventory is limited, so it is better to perform tasks sequentially.

/*
## PLACING BLOCKS

A block can only be built in an empty cell that shares a whole face with a block already on
the grid. Run `near I J K` on the cell you have in mind and read what is around it before you
place anything.

Building one block is `place`, then `fly I J K weld`, then `weld I J K`. A placed block comes
out at minimum integrity — a bare construction site, not a working block — and every block you
place is yours to weld up before you move on to the next one. You need the block's components
in your inventory to weld it.

You have to be within arm's reach of the cell to build in it. If `place` says the cell is out
of reach, fly to a free cell beside it — never into the cell you are about to fill.

Never derive an orientation. Run `orient 'block_type' I J K` and copy the `place` line it
answers with. It knows which faces the block mounts by and which way its conveyor ports look,
and it only offers orientations that actually attach in that cell.

## CONNECTING TWO BLOCKS WITH A CONVEYOR

`route I J K I₂ J₂ K₂` gives the shortest run of conveyor pieces between two built blocks. Each
line of the answer is one piece: the block type, the cell to put it in, and the two directions
its ports have to look. Turn a line into a command with `orient`.

Build the run one piece at a time, in the order `route` gives them:

route 3 2 4 3 6 9
orient 'Conveyor Tube' 3 3 4 ports -Y +Y

then, from what `orient` answered:

place 'Conveyor Tube' 3 3 4 facing +X up +Y
fly 3 3 4 weld
weld 3 3 4

Then run `route` again — from the piece you have just welded to the same target — and it gives
what is left of the run. You never have to remember the rest of it.

* place 'block_type' I J K [facing D up D]
  Build a new block at the given cell. D is one of +X -X +Y -Y +Z -Z.
  The cell must be empty and must share a whole face with a block that is already on the grid.
  You must be within arm's reach of the cell — fly to a free cell beside it, not into it.
  The new block arrives at minimum integrity, so weld it immediately afterwards.
  Do not work the orientation out yourself — run `orient` and copy the line it gives you.
  Example: place 'Gyroscope' 3 2 4 facing +X up +Y

* orient 'block_type' I J K [ports D D₂]
  Ask how to build 'block_type' in that cell. The answer is the whole `place` line, ready to copy.
  Blocks that attach by any face get a line with no orientation at all — that is not an omission.
  With `ports`, the answer is the single line whose conveyor ports look along those two directions.
  Example: orient 'Gyroscope' 3 2 4
  Example: orient 'Curved Conveyor Tube' 3 2 6 ports -Y +Z

* route I J K I₂ J₂ K₂
  The shortest conveyor run between two blocks that already stand on the grid. For every cell of
  the run it gives the piece to put there and the two directions that piece's ports have to look.
  Run it again from the piece you have just built to the same target, and it gives what is left.
  Example: route 3 2 4 3 6 9
*/
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
		{	if(string.IsNullOrEmpty(searchTerm) || searchTerm == "*") return true;
			return text.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private string MyError(Vector3D engineer, string query, List<IMyEntity> matches)
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

		internal CommandResult Memory(TokenParser tp)
		{
			var key = tp.NextString();
			var value = tp.NextString();
			if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value) || !tp.End)
				return "Error: provide a key and value. Usage: memory 'current_task' 'weld reactor at 5 0 2'";
			memory[key] = value;
			return Success("Saved.");
		}

		internal void SetSystemPromptAndMemory()
		{
			var sb = new StringBuilder(Prompt());
			sb.Append("\n## MEMORY\n");
			if (memory.Count > 0)
			{
				foreach (var kv in memory)
					sb.Append("* ").Append(kv.Key).Append(" = ").Append(kv.Value).Append('\n');
			}
			else
			{
				sb.Append("-- none --\n");
			}
			LLE_Loader.SetSystemPrompt(sb.ToString());
		}

		internal CommandResult Select(TokenParser tp)
		{
			var what = tp.NextString();

			var engineer = GetEngineerCenter();
			
			BoundingSphereD S = new BoundingSphereD(engineer, Constants.NearInformationRadius);
			var entities = MyAPIGateway.Entities.GetTopMostEntitiesInSphere(ref S);

			List<IMyEntity> matches = new List<IMyEntity>();

			string category, name;
			
			foreach(var e in entities)
			{	
				if (e.Closed) continue;

				Description(e, out category, out name);

				if(Include(what, name) || Include(what, category)) matches.Add(e);
			}

			if(matches.Count != 1) return MyError(engineer, what, matches);

			var match = matches[0];

			Description(match, out category, out name);

			var grid = match as IMyCubeGrid;
			if(grid != null)
			{	
				Debug.Start(grid);
				selectedGrid = grid;
				selectedAsteroid = null;
				return Success($"Selected {category} {Quote(name)}\nGrid directions: {GridDirections(grid)}");
			}

			var asteroid = match as MyVoxelBase;
			if(asteroid != null)
			{	return "Error: Operations on asteroids are not supported yet.";
				
				selectedGrid = null;
				selectedAsteroid = asteroid;
				return Success($"Selected {category} {Quote(name)}");
			}
			
			return $"Error: you can't select {category} '{name}'";
		}

		internal bool GridIsSet(out string message)
		{	if(selectedGrid == null)
			{	message = "Error: you should select a grid first. Use `select name`";
				return false;
			}
			message = null;
			return true;
		}

		public static bool IsProjection(IMyCubeGrid grid)
		{
			var mcg = grid as MyCubeGrid;
			return mcg != null && mcg.Projector != null;
		}

		internal bool CurrentGridIsProjection(out string message)
		{	if(IsProjection(selectedGrid))
			{	message = "Error: selected grid is a projection preview, not a built object. Not supported for this command.";
				return true;
			}
			message = null;
			return false;
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

			var eqsr = new List<EQSResult>();
			int totalCount = 0;

			EQS.Query(block, GetEngineerCenter(), InteractionKind.Inventory, eqsr, 10);
			totalCount += eqsr.Count;

			if(eqsr.Count != 0)
			{	sb.Append("* Get/Put: ");
				var ip = eqsr.Select(r => r.Cell).ToList();
				AppendList(ip, sb);
			}

			EQS.Query(block, GetEngineerCenter(), InteractionKind.Recharge, eqsr, 10);
			totalCount += eqsr.Count;

			if(eqsr.Count != 0)
			{	sb.Append("* Recharge: ");
				var ip = eqsr.Select(r => r.Cell).ToList();
				AppendList(ip, sb);
			}
			
			EQS.Query(block, GetEngineerCenter(), InteractionKind.GrindWeld, eqsr, 10);
			totalCount += eqsr.Count;
			
			if(eqsr.Count != 0)
			{	sb.Append("* Grind/Weld: ");
				var ip = eqsr.Select(r => r.Cell).ToList();
				AppendList(ip, sb);
			}

			if(totalCount == 0)
			{	sb.Append("-- none --\n");
				sb.Append("(the block is likely fully obstructed by other blocks or rock)\n");
			}
		}

		internal bool IsAtInteractionPoint(IMySlimBlock block, InteractionKind kind, out string message)
		{
			var ec = GetEngineerCenter();
			var r = GetInteractionPointAt(block, kind, ec);
			if(r.HasValue)
			{	message = null;
				return true;
			}
			message = E_BAD_POINT;
			return false;
		}

		EQSResult? GetInteractionPointAt(IMySlimBlock block, InteractionKind kind, Vector3D point)
		{	var eqsr = new List<EQSResult>();
			var cell = block.CubeGrid.WorldToGridInteger(point);
			EQS.QueryOneCell(block, cell, GetEngineerCenter(), kind, eqsr, 1);
			if(eqsr.Count == 0) return null;

			return eqsr[0];
		}

		internal bool IsAtPoint(IMySlimBlock block, List<Vector3I> ip, out string message)
		{
			var engineerCell = selectedGrid.WorldToGridInteger(GetEngineerCenter());

			if(ip.Contains(engineerCell))
			{	message = null;
				return true;
			}

			message = E_BAD_POINT;
			return false;
		}

		internal bool InProgress()
		{	return coroutineStack.Count > 0;
		}

		// Dispose the whole coroutine stack. The command is over — nothing will resume.
		internal void AbortCommand()
		{	foreach(var c in coroutineStack) (c as IDisposable)?.Dispose();
			coroutineStack.Clear();
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
					AbortCommand();
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

				// Top-level coroutine ended without yielding a result. Every command must answer
				// the LLM: LLM.Tick() dequeues the batch head only in OnCommandFinished, so a
				// silent end would re-run this command every tick, forever.
				if(coroutineStack.Count == 0)
				{	MyConsole.Add("!yield break!", Color.DarkRed);
					return Incomplete("Command stopped early.");
				}
			}
			return null;
		}

		internal CommandResult Execute(string command)
		{
			CommandResult result = null;

			var tp = new TokenParser(command);

			// A coroutine command is still running (LLM.Tick() only calls this when idle, so
			// this is a manual chat command). Pushing now would stack the new coroutine on top
			// of the running one, and the first result would dispose the whole stack — which
			// LLM.Tick() would then charge to the LLM's own in-flight command.
			if(coroutineStack.Count != 0)
				return "Error: another command is still running. Wait for it to finish.";

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
			else if(tp.Match("Projection"))
			{	result = Projection();
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
			else if(tp.Match("Memory"))
			{	result = Memory(tp);
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
			else if(tp.Match("Place"))
			{	result = Place(tp);
			}
			else if(tp.Match("Orient"))
			{	result = Orient(tp);
			}
			else if(tp.Match("Route"))
			{	coroutineStack.Push(Route(tp));
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
