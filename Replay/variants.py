#!/usr/bin/env python3
"""Build system prompt variants out of the dumped one, so they cannot drift away from it.

Each variant is a list of (anchor, replacement) pairs. A missing anchor is an error, not a warning:
the prompt has changed and the variant has to be rewritten.
"""

from pathlib import Path

HERE = Path(__file__).resolve().parent

TASKING = "* **Tasking**: Execute instructions from [GAME CHAT]. If no tasks are pending, call `pause` tool."

PERSISTENCE = ("* **Persistence**: `memory` survives a context reset, notes do not."
               " Whatever you must not lose goes into `memory`.")

ON_WARNING = (" A warning that the context is filling up is an order to write the task and the"
              " place you stopped into `memory`. It is never a reason to pause: an unfinished task"
              " in `memory` is still yours, and after a reset you resume it.")

SAVE_TASK = (" A task goes into `memory` the moment you take it, and that entry is overwritten"
             " when it is done — the context is wiped without warning, and a task that is not in"
             " `memory` is a task you abandon halfway.")

VARIANTS = {
    "base": [],

    # Measured at 77% against 99% for the wording that is in the prompt now: as long as writing
    # text is allowed at all, roughly one turn in four ends as text. Kept as the floor to beat.
    "may-write-text": [
        ("""Write no text at all. Your answer is tool calls and nothing else. Tool calls go through the tool interface
only; a call written as text is not a call.""",
         """Tool calls go through the tool interface only. A call written as text is not a call.

Write the plan as text first, then call the tools."""),
    ],

    # On the 70% warning the bot wrote its plan into a note and paused. Notes do not survive a
    # reset, and the task, which lived only in the context, was lost with it. The sentence now in
    # the prompt calls memory on 18 turns out of 20 against 1 without it; this is that floor.
    "no-warning-rule": [(PERSISTENCE + ON_WARNING, PERSISTENCE)],

    # Saving the task when it arrives instead of when the warning comes. Never called memory once
    # on the turn the task arrives, so the rule is written where it does not fire.
    "save-task": [(TASKING, TASKING + SAVE_TASK)],
}


def main():
    base = (HERE / "system.txt").read_text()
    out = HERE / "variants"
    out.mkdir(exist_ok=True)

    for name, edits in VARIANTS.items():
        text = base
        for anchor, replacement in edits:
            if anchor not in text:
                raise SystemExit(f"{name}: anchor not found in system.txt:\n{anchor[:80]}...")
            text = text.replace(anchor, replacement)

        path = out / f"{name}.txt"
        path.write_text(text)
        print(f"{path.name:<20} {len(text)} chars")


if __name__ == "__main__":
    main()
