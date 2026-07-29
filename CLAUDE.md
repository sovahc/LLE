# Space Engineers (LLE mod)

- Main mod: `~/Projects/LLE/LLE/` — C# 6 only. Loader: `~/Projects/LLE/Loader/`.
- Build command: `cd ~/Projects/LLE/LLE/Data/Scripts/LLE && dotnet build LLE.csproj 2>&1 | tail -20`
- The mod is single-threaded — do not raise multithreading concerns in reviews.

Reference locations:
- Existing mods: `~/Projects/SpaceEngineers_mods/`, `~/Projects/SpaceEngineers_mods_selected/`
- Game API and `*.sbc` definitions: `~/Projects/SpaceEngineers/`
- Old source reserve: `~/Projects/SpaceEngineers_Source/`

Mod API whitelist reference (Roslyn `WhitelistDiagnosticAnalyzer`, populated via `MyScriptCompiler.Static.Whitelist.OpenBatch()`):
- `~/Projects/SpaceEngineers_Source/Sources/Sandbox.Game/MySandboxGame.cs` (main)
- `~/Projects/SpaceEngineers_Source/Sources/SpaceEngineers.Game/MySpaceGameCustomInitialization.cs` (game-specific)

Decompiled game DLLs (`.cs` files) live next to their source DLL in `~/Projects/SpaceEngineers/Bin64/`.
