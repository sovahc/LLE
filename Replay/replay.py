#!/usr/bin/env python3
"""Rebuild the model's context from a game log and replay one turn against the local server.

The log holds everything the mod sent: the user messages, the winning stream's tool calls and
every command result. What it does not hold is the exact request JSON, so the rebuild is faithful
in content but not byte-identical. It is enough to compare system prompts against each other,
which all see the same rebuilt context.

    ./replay.py LOG --list
    ./replay.py LOG --turn 42 --system system.txt --system variant.txt --n 20
"""

import argparse
import json
import re
import sys
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent

RECORD = re.compile(r'^\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d+ - Thread: *\d+ -> +(.*)$')
PICK = re.compile(r'^\[PICK\] (\d)([ *]) (\S+)')
STREAM = re.compile(r'^llm(Content|ToolCall)\[(\d)\]:\s?(.*)$', re.S)
CALL = re.compile(r'^([a-z_]+)\((.*)\)$', re.S)
ARGUMENT = re.compile(r',\s+(?=[a-z_]\w*=)')

NOT_EXECUTED = "Not executed: an earlier call in this batch failed."


def records(path):
    """The log's timestamped records, continuation lines folded into the record they belong to."""
    current = None
    for line in Path(path).read_text(errors="replace").splitlines():
        head = RECORD.match(line)
        if head:
            if current is not None:
                yield current
            current = head.group(1)
        elif current is not None:
            current += "\n" + line
    if current is not None:
        yield current


def parse_call(text):
    m = CALL.match(text.strip())
    if not m:
        return None

    name, body = m.group(1), m.group(2).strip()
    arguments = {}

    if body:
        for piece in ARGUMENT.split(body):
            key, _, value = piece.partition("=")
            key = key.strip()
            value = value.strip()
            if re.fullmatch(r'-?\d+', value):
                arguments[key] = int(value)
            elif re.fullmatch(r'-?\d+\.\d+', value):
                arguments[key] = float(value)
            elif value in ("true", "false"):
                arguments[key] = value == "true"
            else:
                arguments[key] = value

    return {"name": name, "arguments": arguments}


# What the mod writes to the model rather than to the command answer. A report can arrive after a
# command has finished, and then it stands inside that command's block in the log.
REPORT = ("[GAME CHAT]", "[VISION]", "[STATUS]", "[CONTEXT", "[ERROR]", "!Warning", "!WARNING", "!ERROR")


def split_report(text):
    """A toLLM block into (user text, command results). Results are the lines opened by an arrow."""
    user, results = [], []
    inside = False

    for line in text.splitlines():
        if line.startswith("→ "):
            _, _, rest = line.partition(": ")
            results.append([rest])
            inside = True
        elif line.startswith(REPORT) or line.startswith("[YOU]:"):
            inside = False
            if not line.startswith("[YOU]:"):
                user.append(line)
        elif inside:
            results[-1].append(line)
        elif line.strip():
            user.append(line)

    return "\n".join(user).strip(), ["\n".join(r).strip() for r in results]


class Turn:
    def __init__(self, index, user, assistant, results):
        self.index = index
        self.user = user
        self.assistant = assistant
        self.results = results

    @property
    def calls(self):
        return self.assistant["calls"] if self.assistant else []


def parse_log(path):
    """Turns in order. Each turn is one user message and the answer that was picked for it."""
    turns = []
    streams = {}
    winner = None
    silent = {}

    def flush(user_text, results):
        answer = None
        if streams:
            index = winner if winner is not None else min(streams)
            answer = streams.get(index)
        turns.append(Turn(len(turns), user_text, answer, results))

    pending_user, pending_results = None, []
    started = False

    for record in records(path):
        if not record.startswith("LLE "):
            continue
        body = record[4:]

        stream = STREAM.match(body)
        if stream:
            kind, index = stream.group(1), int(stream.group(2))
            slot = streams.setdefault(index, {"content": "", "calls": []})
            if kind == "Content":
                slot["content"] = stream.group(3).strip()
            else:
                call = parse_call(stream.group(3))
                if call:
                    slot["calls"].append(call)
            continue

        pick = PICK.match(body)
        if pick:
            index = int(pick.group(1))
            streams.setdefault(index, {"content": "", "calls": []})
            if pick.group(2) == "*":
                winner = index
            if pick.group(3) == "no":
                silent[len(turns)] = silent.get(len(turns), 0) + 1
            continue

        if body.startswith("toLLM: "):
            user_text, results = split_report(body[len("toLLM: "):])
            if started:
                flush(pending_user, pending_results)
            started = True
            streams, winner = {}, None
            pending_user, pending_results = user_text, results

    if started:
        flush(pending_user, pending_results)

    for turn in turns:
        turn.silent = silent.get(turn.index, 0)

    return turns


def messages(turns, upto, system):
    """Everything the model saw before answering turn `upto`."""
    out = [{"role": "system", "content": system}]
    call_id = 0

    for turn in turns[:upto + 1]:
        results = turn.results
        if results:
            # The results of the previous turn's calls arrive before this turn's user message.
            for i, text in enumerate(results):
                out.append({"role": "tool", "tool_call_id": f"c{call_id + i}", "content": text})

        out.append({"role": "user", "content": turn.user or "..."})

        if turn is turns[upto]:
            break

        calls = turn.calls
        assistant = {"role": "assistant", "content": turn.assistant["content"] if turn.assistant else ""}
        if calls:
            assistant["tool_calls"] = [
                {"id": f"c{call_id + i}", "type": "function",
                 "function": {"name": c["name"], "arguments": json.dumps(c["arguments"])}}
                for i, c in enumerate(calls)
            ]
        out.append(assistant)
        call_id += len(calls)

    return fix_tool_answers(out)


def fix_tool_answers(out):
    """A tool message is only legal right after the assistant that asked for it, one per call."""
    fixed = []
    expected = []

    for message in out:
        if message["role"] == "tool":
            if not expected:
                continue
            message["tool_call_id"] = expected.pop(0)
            fixed.append(message)
            continue

        while expected:
            fixed.append({"role": "tool", "tool_call_id": expected.pop(0), "content": NOT_EXECUTED})

        fixed.append(message)
        if message["role"] == "assistant":
            expected = [c["id"] for c in message.get("tool_calls", [])]

    while expected:
        fixed.append({"role": "tool", "tool_call_id": expected.pop(0), "content": NOT_EXECUTED})

    return fixed


def memory_of(turns, upto):
    remembered = {}
    for turn in turns[:upto]:
        for call in turn.calls:
            if call["name"] == "memory":
                remembered[call["arguments"].get("key", "")] = call["arguments"].get("value", "")

    if not remembered:
        return "\n## MEMORY\n-- none --\n"

    return "\n## MEMORY\n" + "".join(f"* {k} = {v}\n" for k, v in remembered.items())


def ask(url, model, body):
    request = urllib.request.Request(
        url, data=json.dumps(body).encode(), headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=600) as response:
        return json.loads(response.read())


def run(args):
    turns = parse_log(args.log)

    if args.list:
        for turn in turns:
            mark = f"  {turn.silent} silent" if turn.silent else ""
            calls = "; ".join(f"{c['name']}(...)" for c in turn.calls) or "-- no call --"
            print(f"{turn.index:4}  {len(turn.calls)} call(s)  {calls[:90]}{mark}")
        print(f"\n{len(turns)} turns, {sum(1 for t in turns if t.silent)} with a silent stream")
        return

    schema = json.loads((HERE / "tools.json").read_text())
    picked = args.turn if args.turn is not None else [t.index for t in turns if t.silent]
    picked = picked if isinstance(picked, list) else [picked]

    print(f"turns {picked}, {args.n} samples each, model {args.model}\n")
    print(f"{'variant':<24} {'turn':>5} {'called':>8} {'calls/answer':>13}  tools")

    for path in args.system:
        system = Path(path).read_text()
        for index in picked:
            memory = "\n## MEMORY\n" + "".join(f"* {m}\n" for m in args.memory) \
                if args.memory else memory_of(turns, index)

            body = {
                "model": args.model,
                "max_tokens": args.max_tokens,
                "stream": False,
                "chat_template_kwargs": {"enable_thinking": True},
                "messages": messages(turns, index, system + memory),
                "tools": schema,
            }

            called, total, names = 0, 0, {}
            for _ in range(args.n):
                answer = ask(args.url, args.model, body)
                calls = answer["choices"][0]["message"].get("tool_calls") or []
                called += 1 if calls else 0
                total += len(calls)
                for call in calls:
                    name = call["function"]["name"]
                    names[name] = names.get(name, 0) + 1

            ranked = sorted(names.items(), key=lambda kv: -kv[1])
            histogram = " ".join(f"{name}:{count}" for name, count in ranked)

            print(f"{Path(path).name:<24} {index:>5} {called * 100 // args.n:>7}%"
                  f" {total / args.n:>13.2f}  {histogram}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("log")
    parser.add_argument("--list", action="store_true", help="print the turns and stop")
    parser.add_argument("--turn", type=int, action="append",
                        help="turn to replay; repeatable. Default: every turn a stream stayed silent on")
    parser.add_argument("--system", action="append", default=None,
                        help="system prompt file; repeatable, one column per file")
    parser.add_argument("--n", type=int, default=10, help="samples per variant per turn")
    parser.add_argument("--memory", action="append", default=[],
                        help="'key = value' line to put in MEMORY instead of what the log remembered")
    parser.add_argument("--url", default="http://localhost:8080/v1/chat/completions")
    parser.add_argument("--model", default="google/gemma-4-31b-it")
    parser.add_argument("--max-tokens", type=int, default=4000)

    args = parser.parse_args()
    if not args.system:
        args.system = [str(HERE / "system.txt")]

    run(args)


if __name__ == "__main__":
    main()
