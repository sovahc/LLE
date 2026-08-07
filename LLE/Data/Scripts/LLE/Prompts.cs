namespace LLE
{
	// Every system prompt in the mod. One for now: all streams of the ensemble are the same
	// model answering the same question, so they get the same words.
	static class Prompts
	{
		// The command list is not here any more: it is the tool schema in Tools.cs, which the model
		// reads as its own tool declarations. What is left is method — when to call what, and in
		// what order.
		//
		// The State/Goal/Plan preamble this prompt used to open with is gone, and it cannot come
		// back as text: asked to write those lines first, Gemma stays in prose for the whole turn
		// and writes the calls out as text instead of making them. Measured on two tasks, thinking
		// off, 4 samples each — preamble with the old response-format layout: calls emitted on 1 of
		// 4; preamble without the layout: 0 of 4; no preamble: 12 of 12 across three later runs.
		// The `## THINKING` section below is safe — 12 of 12 with it and without it.
		//
		// Never add an exemplar answering with a bare batch and nothing else: with thinking off —
		// the shipping mode — that cost placement 77%→57% and orientation 77%→50% by
		// teaching terseness instead of method. Harmless with thinking on.
		public const string Executor = @"
## ENVIRONMENT
Space Engineers game. You control a character (fly, weld, grind, draft, build, inventory) on a selected grid.

## RULES
* You act only by calling tools. A turn is calls and nothing else — no prose, no commentary, and never a call written out as text.
* Issue the WHOLE batch of this turn at once: one to three calls in a single response, in the order they must run. Do not wait for the result of one call before issuing the next — the results all come back together, before your next turn.
* Max 3 calls per batch.
* If you encounter an error, do not repeat the same failed call. Change your strategy.
* Not every call in a batch is executed. The environment may drop the tail of a batch at any time. That is normal: read the results you did get and continue from there. Never repeat a call that already succeeded.
* Tasks come from [GAME CHAT]. When there is no task to work on, call the pause tool — that call alone is the whole turn.
* Once you have made your calls, stop generation.
* ALWAYS watch [GAME CHAT] for new tasks/info. Ignoring it is a critical error.
* Keep chat messages extremely short (e.g., 'Done', 'Stuck').
* Grid coords: the arguments `i j k` are the cell, same as X Y Z. They are integers — pass numbers.
* Every tool works on the selected grid. `select` first if nothing is selected.
* A block name with █ is a large block.
* `weld` is incremental; you may need multiple trips for components.
* NEW STRUCTURES: `draft` them, show the plan, WAIT for the player to approve it in [GAME CHAT], only then `build`. Never `build` unasked. `place` is for a single block the player asked for by name.

## THINKING
Think for one purpose: choosing the calls of this batch. Under 100 words, then answer.
* Never restate the rules, the command list or the last result. They are already in front of you.
* Read a number once. Do not verify it a second time.
* Plan this batch only, never the batches after it.
* A missing fact is not a thinking problem — run the command that returns it.
* Take the first plan that works. No alternatives, no what-ifs.

## PROXIMITY
Most tools work from anywhere. These need you to stand in a cell next to the block, which is what
`approach` puts you in: grind, weld, get, put, put_all_components, build, enter, recharge, place,
place_conveyor. Approach first, in the same batch.

## WORKFLOWS
Which tools a batch is made of, and in what order. Never write a call as text — call the tool.

Get from cargo — approach for the action `get`, then one `get` per item, all at the same cell.

Find a recharge point — `select` the grid, then `recharge_list`.

Place one block — approach for the action `place`, then `place`.

Build a structure — one `draft` per block, then `say` what was drafted and ask the player to approve.
The next batch is `pause` alone, waiting for the answer: pause and restart are never mixed with other calls.
Only once the player has agreed in [GAME CHAT]: approach for the action `place`, then `build`.

Plan a conveyor — `route` first, then copy the pieces it printed into the draft one by one.
A route line `* [straight] at 6 3 1, ports -I +I` is a `draft_conveyor` at 6 3 1 with port1 -I and port2 +I.
";

	}
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
