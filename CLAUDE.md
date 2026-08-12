# Space Engineers (LLE mod)

- Main mod: `~/Projects/LLE/LLE/` — C# 6 only. Loader: `~/Projects/LLE/Loader/`.
- Build command: `cd ~/Projects/LLE/LLE/Data/Scripts/LLE && dotnet build LLE.csproj 2>&1 | tail -20`
- The mod is single-threaded — do not raise multithreading concerns in reviews.

Debug module (`Loader/Debug.cs`, off unless `DebugPort` is set in `LLELoader.json`): a TCP line
protocol for running the game without a human. `Loader/game.py` drives it — `up` starts the game and
loads the last save, `task TEXT` gives the bot a task and returns with its answer, `shot` takes a
screenshot, `quit` exits. Changes to the loader or the mod need a game restart to take effect.

Prompt bench (`~/Projects/LLE/Replay/`): rebuilds a turn's context from a game log and replays it against a local LLM server to compare system prompt variants.

Reference locations:
- Existing mods: `~/Projects/SpaceEngineers_mods/`, `~/Projects/SpaceEngineers_mods_selected/`
- Game API and `*.sbc` definitions: `~/Projects/SpaceEngineers/`
- Old source reserve: `~/Projects/SpaceEngineers_Source/`

Mod API whitelist reference (Roslyn `WhitelistDiagnosticAnalyzer`, populated via `MyScriptCompiler.Static.Whitelist.OpenBatch()`):
- `~/Projects/SpaceEngineers_Source/Sources/Sandbox.Game/MySandboxGame.cs` (main)
- `~/Projects/SpaceEngineers_Source/Sources/SpaceEngineers.Game/MySpaceGameCustomInitialization.cs` (game-specific)

Decompiled game DLLs (`.cs` files) live next to their source DLL in `~/Projects/SpaceEngineers/Bin64/`.
