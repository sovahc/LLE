#!/usr/bin/env python3
"""Drive Space Engineers with the LLE loader's debug channel.

The channel is off unless "DebugPort" is set in LLELoader.json.

    ./game.py up                      start the game and load the last save
    ./game.py status
    ./game.py hold | pass             hold the mod's requests, or let them through
    ./game.py request                 the exact request JSON the mod built
    ./game.py call NAME '{"a": 1}'    answer a held request with one tool call
    ./game.py task TEXT               give the bot a task and wait it out, printing its answers
    ./game.py say TEXT                a chat line from the player; reaches a paused bot too
    ./game.py wait [SECONDS]          block until the bot answers or goes idle
    ./game.py release                 let a held request go to the model after all
    ./game.py shot [PATH]             take a screenshot and copy it out
    ./game.py log [N]                 tail of the current game log
    ./game.py quit
"""

import json
import os
import shutil
import socket
import subprocess
import sys
import time
from pathlib import Path

PORT = int(os.environ.get("LLE_DEBUG_PORT", "8099"))
APP_ID = "244850"

STEAM = Path.home() / ".steam/steam/steamapps"
USER_DATA = STEAM / "compatdata" / APP_ID / "pfx/drive_c/users/steamuser/AppData/Roaming/SpaceEngineers"
SCREENSHOT = USER_DATA / "Screenshots/LLE.png"
LOADER_LOG = STEAM / "common/SpaceEngineers/Bin64/LLELoader.log"


def send(cmd, patience=30, **fields):
    fields["cmd"] = cmd
    try:
        connection = socket.create_connection(("127.0.0.1", PORT), timeout=patience)
    except OSError:
        sys.exit(f"nothing listening on 127.0.0.1:{PORT} — game down, or DebugPort not set")

    with connection as s:
        s.sendall((json.dumps(fields) + "\n").encode())
        answer = b""
        while not answer.endswith(b"\n"):
            part = s.recv(65536)
            if not part:
                break
            answer += part

    reply = json.loads(answer.decode())
    if not reply.get("ok"):
        sys.exit("error: " + reply.get("error", "no answer"))
    return reply


def alive():
    try:
        with socket.create_connection(("127.0.0.1", PORT), timeout=2):
            return True
    except OSError:
        return False


def wait_for(states, timeout=300):
    """Block until the game reports one of `states`; returns the one it reached."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        if alive():
            state = send("status")["state"]
            if state in states:
                return state
        time.sleep(2)
    sys.exit(f"timeout waiting for {'/'.join(states)}")


def start():
    if alive():
        print("already running")
        return
    subprocess.Popen(["steam", "-applaunch", APP_ID],
                     stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    print("starting...")
    wait_for(("menu", "loading", "ingame"), timeout=600)


def up():
    start()
    state = send("status")["state"]
    if state == "menu":
        send("load_last")
        print("loading the last save...")
        state = wait_for(("ingame",), timeout=900)
    elif state != "ingame":
        state = wait_for(("ingame",), timeout=900)
    print("Loading done. Game is ready.")


# The game pushes what the bot says and when it goes idle; nothing here reads the log.
def wait(seconds=60):
    event = send("wait", patience=seconds + 10, timeout=seconds)["event"]
    if event["kind"] == "say":
        print("bot: " + event["text"])
    else:
        print(event["kind"])
    return event["kind"]


def task(text, seconds=120):
    send("chat", text=text)
    deadline = time.time() + seconds
    while time.time() < deadline:
        if wait(int(deadline - time.time())) in ("pause", "timeout"):
            return


# The mod waits out every stream it started, so all held channels get the same answer.
def answer(name, arguments):
    held = send("status")["held"]
    if not held:
        sys.exit("no request is being held — is the mode set to hold?")

    for channel in held:
        send("call", channel=channel, calls=[{"name": name, "arguments": arguments}])
    print(f"{name} sent to channel " + ", ".join(str(c) for c in held))


def shot(destination=None):
    before = SCREENSHOT.stat().st_mtime if SCREENSHOT.exists() else 0
    send("screenshot")

    deadline = time.time() + 30
    while time.time() < deadline:
        if SCREENSHOT.exists() and SCREENSHOT.stat().st_mtime != before:
            # The file appears empty and is filled afterwards: wait for the size to settle.
            size = -1
            while size != SCREENSHOT.stat().st_size or size == 0:
                size = SCREENSHOT.stat().st_size
                time.sleep(0.5)

            if destination:
                shutil.copy(SCREENSHOT, destination)
                print(destination)
            else:
                print(SCREENSHOT)
            return
        time.sleep(0.5)
    sys.exit("no screenshot appeared")


def game_log():
    logs = sorted(USER_DATA.glob("SpaceEngineers_*.log"), key=lambda p: p.stat().st_mtime)
    if not logs:
        sys.exit("no game log found")
    return logs[-1]


def main(argv):
    if not argv:
        sys.exit(__doc__)

    cmd, rest = argv[0], argv[1:]

    if cmd == "up":
        up()
    elif cmd == "start":
        start()
    elif cmd == "status":
        print(json.dumps(send("status")))
    elif cmd in ("hold", "pass"):
        send("mode", value=cmd)
        print(cmd)
    elif cmd == "request":
        print(send("request", channel=int(rest[0]) if rest else 0)["request"])
    elif cmd == "call":
        if not rest:
            sys.exit("call needs a tool name")
        answer(rest[0], json.loads(rest[1]) if len(rest) > 1 else {})
    elif cmd == "say":
        send("chat", text=" ".join(rest))
        print("sent")
    elif cmd == "task":
        task(" ".join(rest))
    elif cmd == "wait":
        wait(int(rest[0]) if rest else 60)
    elif cmd == "release":
        send("release", channel=int(rest[0]) if rest else 0)
        print("released to the model")
    elif cmd == "shot":
        shot(rest[0] if rest else None)
    elif cmd == "log":
        path = game_log()
        lines = path.read_text(errors="replace").splitlines()
        print(path)
        print("\n".join(lines[-int(rest[0] if rest else 60):]))
    elif cmd == "loader-log":
        print(LOADER_LOG.read_text(errors="replace"))
    elif cmd == "quit":
        send("quit")
        print("quitting")
    else:
        sys.exit(__doc__)


if __name__ == "__main__":
    main(sys.argv[1:])
