#!/usr/bin/env python3
"""Build system prompt variants out of the dumped one, so they cannot drift away from it.

Each variant is a list of (anchor, replacement) pairs. A missing anchor is an error, not a warning:
the prompt has changed and the variant has to be rewritten.
"""

from pathlib import Path

HERE = Path(__file__).resolve().parent

BATCH_EXAMPLE = """Calls that belong to one step go in one turn, in the order they must run — for example
`approach(i=-1, j=1, k=-7, action=put)` and then `put_all_components(i=-1, j=1, k=-7)`.
The first call that does not end in OK drops the rest of the turn."""

VARIANTS = {
    "base": [],

    # Measured at 77% against 99% for the wording that is in the prompt now: as long as writing
    # text is allowed at all, roughly one turn in four ends as text. Kept as the floor to beat.
    "may-write-text": [
        ("""Write no text at all. Your answer is tool calls and nothing else — a turn spent on text moves
nothing, and you will be asked the same question again. Tool calls go through the tool interface
only; a call written as text is not a call.""",
         """Tool calls go through the tool interface only. A call written as text is not a call.

Write the plan as text first, then call the tools."""),
    ],

    # The batching example has never once produced a batch. This drops it, to see what it costs.
    "no-batch-example": [
        (BATCH_EXAMPLE, "The first call that does not end in OK drops the rest of the turn."),
    ],
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
