#!/usr/bin/env python3
"""Measure whether the model can write a list-shaped call, and how often it writes it wrong.

The tool under test replaces `put` in the dumped schema; everything else the model sees is the
recorded context of a real turn. Nothing here touches the mod.

    ./shapes.py LOG --turn 24 --n 20
"""

import argparse
import collections
import json
from pathlib import Path

import replay

HERE = Path(__file__).resolve().parent

DESCRIPTION = ("Put things away. Write down everything you are carrying that has to go into this"
               " block — one entry per item type, all in one call.")

SHAPES = {
    "A": {
        "type": "array",
        "description": "What to put in. One entry per item type.",
        "items": {
            "type": "object",
            "properties": {
                "item": {"type": "string", "description": "Exact item name."},
                "count": {"type": "number", "description": "How many. Omit to put all of them."},
            },
            "required": ["item"],
        },
    },
    "B": {
        "type": "array",
        "description": "One entry per item type: the exact item name, then how many,"
                       " for example 'Steel Plate 50'. The name on its own means all of them.",
        "items": {"type": "string"},
    },
}


def schema_with(shape):
    tools = json.loads((HERE / "tools.json").read_text())

    for tool in tools:
        function = tool["function"]
        if function["name"] != "put":
            continue

        function["description"] = DESCRIPTION
        properties = function["parameters"]["properties"]
        properties.pop("item", None)
        properties.pop("count", None)
        properties["items"] = SHAPES[shape]
        function["parameters"]["required"] = ["i", "j", "k", "items"]

    return tools


def entry_error(shape, entry):
    if shape == "A":
        if not isinstance(entry, dict):
            return "entry is not an object"
        name = entry.get("item")
        if not isinstance(name, str) or not name.strip():
            return "no item name"
        if "count" in entry and not isinstance(entry["count"], (int, float)):
            return "count is not a number"
        return None

    if not isinstance(entry, str) or not entry.strip():
        return "entry is not a name"

    head, _, tail = entry.strip().rpartition(" ")
    if head and tail.replace(".", "", 1).isdigit():
        return None if head.strip() else "no item name"
    return None


def check(shape, call):
    """None if the call is well formed, otherwise what is wrong with it."""
    try:
        arguments = json.loads(call["function"]["arguments"])
    except json.JSONDecodeError as e:
        return f"arguments are not JSON ({e.msg})", 0

    for key in ("i", "j", "k"):
        if not isinstance(arguments.get(key), (int, float)):
            return f"no {key}", 0

    items = arguments.get("items")
    if items is None:
        return "no items", 0
    if not isinstance(items, list):
        return f"items is {type(items).__name__}, not a list", 0

    for entry in items:
        error = entry_error(shape, entry)
        if error:
            return error, len(items)

    return None, len(items)


def run(args):
    turns = replay.parse_log(args.log)
    system = (HERE / "variants" / "base.txt").read_text() + replay.memory_of(turns, args.turn)
    context = replay.messages(turns, args.turn, system)

    for shape in args.shape:
        errors = collections.Counter()
        sizes = collections.Counter()
        examples = []

        body = {
            "model": args.model,
            "max_tokens": args.max_tokens,
            "stream": False,
            "chat_template_kwargs": {"enable_thinking": True},
            "messages": context,
            "tools": schema_with(shape),
        }

        for _ in range(args.n):
            answer = replay.ask(args.url, args.model, body)
            calls = answer["choices"][0]["message"].get("tool_calls") or []
            put = next((c for c in calls if c["function"]["name"] == "put"), None)

            if put is None:
                errors["did not call put: " + ("|".join(c["function"]["name"] for c in calls)
                                               or "no call at all")] += 1
                continue

            error, size = check(shape, put)
            sizes[size] += 1
            if error:
                errors[error] += 1
                if len(examples) < 3:
                    examples.append(put["function"]["arguments"])

        good = args.n - sum(errors.values())
        print(f"\nshape {shape}: {good}/{args.n} well formed")
        print(f"  entries per call: {dict(sorted(sizes.items()))}")
        for what, count in errors.most_common():
            print(f"  {count:3}  {what}")
        for text in examples:
            print(f"  bad: {text[:220]}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log")
    parser.add_argument("--turn", type=int, required=True)
    parser.add_argument("--shape", action="append", default=None, choices=list(SHAPES))
    parser.add_argument("--n", type=int, default=20)
    parser.add_argument("--url", default="http://localhost:8080/v1/chat/completions")
    parser.add_argument("--model", default="/home/cat/LLM/gemma-4-26B-A4B-it-qat-UD-Q4_K_XL.gguf")
    parser.add_argument("--max-tokens", type=int, default=4000)

    args = parser.parse_args()
    args.shape = args.shape or list(SHAPES)
    run(args)


if __name__ == "__main__":
    main()
