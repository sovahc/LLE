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
## ENVIRONMENT
Space Engineers game. You control a character (fly, weld, grind, draft, build, inventory) on a selected grid.

## RULES
* Before the <execute> block, write exactly these three lines, one short sentence each:
   State: what the last command result told you.
   Goal: what the chat asked you to do, in your own words.
   Plan: the commands you are about to run, and why in that order.

* RESPONSE FORMAT (Choose based on command type):

   [TYPE A: TRIVIAL] (fly, say, status, memory, select, info, distance, enter, exit, position)
   Use ONLY this format:
   State: [last result]
   <execute>[command]</execute>

   [TYPE B: STRATEGIC] (draft, route, build, complex sequences, error recovery, multi-step tasks)
   Use the FULL format:
   State: [last result]
   Goal: [your goal]
   Plan: [your plan]
   [Thinking block (max 100 words)]
   <execute>[command]</execute>

* For [TYPE A] commands, DO NOT write Goal, Plan, or Thinking. Go straight to <execute>.
* For [TYPE B] commands, follow the full protocol.
* Max 5 commands per batch.
* If you encounter an error, do not repeat the same failed command. Change your strategy.
* Tasks from [GAME CHAT]. No task? Execute `<execute>pause</execute>` and stop.
* After `<execute>`, stop generation.
* ALWAYS watch [GAME CHAT] for new tasks/info. Ignoring it is a critical error.
* Keep chat messages extremely short (e.g., 'Done', 'Stuck').
* Grid coords: `I J K` same as X Y Z.
* `weld` is incremental; you may need multiple trips for components.
* NEW STRUCTURES: `draft` them, show the plan, WAIT for the player to approve it in [GAME CHAT], only then `build`. Never `build` unasked. `place` is for a single block the player asked for by name.

## THINKING
Think for one purpose: choosing the commands of this batch. Under 100 words, then answer.
* Never restate the rules, the command list or the last result. They are already in front of you.
* Read a number once. Do not verify it a second time.
* Plan this batch only, never the batches after it.
* A missing fact is not a thinking problem — run the command that returns it.
* Take the first plan that works. No alternatives, no what-ifs.

## COMMANDS

All commands use Grid Coordinates (I, J, K).
If a command returns an error, use the Plan to fix the situation in the next batch

### 1. System
* pause - Pause bot (resumes on event).
* restart - Reset context (memory preserved).

### 2. Movement
* fly I J K [headfirst] - Land at specific cell.
* fly forward|backward|left|right|up|down N - Fly N cells relative to grid.
* approach I J K [action] (action: grind, weld, get, put, recharge, enter, place) - Fly to block interaction point.

### 3. Remote (No proximity required)
[Block name with █ is a large block]
* memory 'key' 'value' - Persistent key-value pair.
* select 'name' - Select grid. Returns axes (e.g. up=+Y).
* position - Current coords on selected grid.
* status - your Health, Hydrogen, Energy (no Oxygen needed).
* say 'msg' - Send chat message.
* exit - Leave cockpit/seat.
* overview - List blocks by category.
* integrity - Show damaged blocks.
* projection - Build status of projection.
* near [I J K] / free [I J K] - 3x3x3 cube around coords.
* inventory - List your items.
* inventories - List all grid inventories.
* inventory I J K - List one container inventory.
* recharge list - List recharge blocks.
* search item/block 'substring' [N] - Search nearby grids.
* distance I J K [I2 J2 K2] - Distance to block or between points.
* points I J K - List interaction points.
* info I J K - Detailed block info (size, state, etc).
* route from I J K to I2 J2 K2 - Shortest conveyor tube path (returns required types). Ends may be drafted blocks; drafted cells are treated as occupied.
* transfer [N|all] 'item' from I J K to I2 J2 K2 - Move items.
* draft 'type' at I J K [facing forward|backward|left|right] [up|down] - Plan a block. Nothing is built; the player sees the whole draft as a projection.
* draft conveyor I J K dir1 dir2 [square|round|reinforced] - Draft a tube piece.
* draft / draft show - List the draft. draft undo - Drop the last block. draft clear - Drop all.

### 4. Proximity (Must be in adjacent cell)
* grind/weld I J K - Grind/weld block.
* get N 'item' from I J K - Get from container.
* put [N|all|all components] 'item' into I J K - Put in container.
* build - Build every drafted block within reach (needs approval first). Repeat after moving.
* enter I J K - Enter cockpit/seat.
* recharge from I J K - Recharge at block.

## WORKFLOWS
Get from cargo:
<execute>
fly to 5 3 1 for get
get 100 'Steel Plate' from 5 3 1
get 10 'Small Steel Tube' from 5 3 1
</execute>

Find recharge:
<execute>
select 'Station'
recharge list
</execute>

Place block
<execute>
approach 4 4 3 for place
place 'Interior Pillar' at 4 4 3 facing forward
</execute>

Build a structure — plan, ask, wait:
<execute>
draft 'Light Armor Block' at 4 4 3
draft 'Light Armor Block' at 4 4 4
say 'Drafted 2 armor blocks at 4 4 3 and 4 4 4. Approve?'
pause
</execute>

Plan a conveyor: `route` first, then copy its pieces into the draft as they are printed.
`* [straight] at 6 3 1, ports -I +I` becomes `draft conveyor 6 3 1 -I +I`.
Then, and only after the player agrees in [GAME CHAT]:
<execute>
approach 4 4 3 for place
build
</execute>
";
}

/*

* place 'type' at I J K facing [forward|backward|left|right] - Build block (requires welding).
* place conveyor I J K dir1 dir2 [square|round|reinforced] - Build a tube piece (requires welding).



* power — state of the grid's power system: reactors/batteries/solar/wind, total output, battery charge

Radio subsystem (vision like)
* approach I J K — fly close to coordinates, but without A* into the grid
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

! Pathfinding: safest (default) / shortest / scouting / prefer open space
? log - Return history of executed commands and their results
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
			LLE_Loader.SetSystemPrompt(sb.ToString(), LLM.StopWorld);
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
				
				//selectedGrid = null;
				//selectedAsteroid = asteroid;
				//return Success($"Selected {category} {Quote(name)}");
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
			else if(tp.Match("Position")) result = Position();
			else if(tp.Match("Overview")) result = Overview();
			else if(tp.Match("Integrity")) result = Integrity();
			else if(tp.Match("Projection")) result = Projection();
			else if(tp.Match("Select")) result = Select(tp);
			else if(tp.Match("Fly"))
			{	var dir = MatchDirection(tp);
				if(dir != null) coroutineStack.Push(Fly_Direction_N(dir, tp));
				else            coroutineStack.Push(Fly(tp));
			}
			else if(tp.Match("Approach")) coroutineStack.Push(Approach(tp));
			else if(tp.Match("Grind")) coroutineStack.Push(Grind(tp));
			else if(tp.Match("Weld")) coroutineStack.Push(Weld(tp));
			else if(tp.Match("Near")) result = Near(tp);
			else if(tp.Match("Free")) result = Near(tp, true);
			else if(tp.Match("Inventory")) result = Inventory(tp);
			else if(tp.Match("Inventories")) result = Inventories();
			else if(tp.Match("Get")) coroutineStack.Push(Get(tp));
			else if(tp.Match("Put")) coroutineStack.Push(Put(tp));
			else if(tp.Match("Status")) result = Success(status.ReportAll());
			else if(tp.Match("Screenshot")) coroutineStack.Push(Screenshot());
			else if(tp.Match("Say")) result = Say(tp);
			else if(tp.Match("Memory")) result = Memory(tp);
			else if(tp.Match("Transfer")) coroutineStack.Push(Transfer(tp));
			else if(tp.Match("Search")) result = Search(tp);
			else if(tp.Match("Distance")) result = Distance(tp);
			else if(tp.Match("Points")) result = Points(tp);
			else if(tp.Match("Info")) result = Info(tp);
			else if(tp.Match("Place"))
			{	coroutineStack.Push(tp.Match("conveyor") ? PlaceConveyor(tp) : Place(tp));
			}
			else if(tp.Match("Draft")) result = tp.Match("conveyor") ? DraftConveyor(tp) : Draft(tp);
			else if(tp.Match("Build")) coroutineStack.Push(Build());
			else if(tp.Match("Route")) coroutineStack.Push(Route(tp));
			else if(tp.Match("Enter")) result = Enter(tp);
			else if(tp.Match("Exit")) result = Exit(tp);
			else if(tp.Match("Recharge"))
			{	
				var action = tp.NextString();

				if(action == "list")
					result = GetRechargePoints(tp);
				else if (action == "from")
					coroutineStack.Push(Recharge(tp));
				else return "Error: expected 'list' or 'from'";
			}
			else
			{	result = $"Unknown command '{tp.NextString()}'.";
			}

			return result;
		}
	}
}
