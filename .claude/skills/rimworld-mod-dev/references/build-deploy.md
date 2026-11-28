# Building and deploying

## Paths

- RimWorld: `D:\SteamLibrary\steamapps\common\RimWorld` (1.7, game data under `Data\`, `Assembly-CSharp.dll` in the install root). `Directory.Build.props` sets `SteamLibraryDirectory` to `D:\SteamLibrary`; override it in a git-ignored `Directory.Build.local.props` on another machine.
- `dotnet` may not be on `PATH`: use `~/.dotnet/dotnet.exe`. If a build step prints nothing at all, check that it actually ran.
- Mods deploy to `RimWorld\Mods\<ProjectName>`. BridgeTools deploys to `RimWorld\BridgeTools\Ordo` (companions load from there, not from `Mods`).

## Building

```
dotnet build <Mod>/<Mod>.csproj                      # build into <Mod>/out
dotnet build <Mod>/<Mod>.csproj -t:CopyToRimworld    # build, then deploy
```

- The configuration defaults to Debug, and every configuration builds to `out/Assemblies`, so `-c` isn't needed. (Before 2026-11-21 the props file only set the output path for an explicit `-c Debug`, and a build without it deployed a stale DLL. Old notes may still say "always `-c Debug`".) The Debug build embeds its PDB and isn't optimized, which is what hot reload and the debugger need.
- `CopyToRimworld` depends on `Build`, then replaces `RimWorld\Mods\<Mod>` with `out/`. `CopyToRimworldDebug` does the same into `RimWorldDebugDirectory`. Pass `-p:DeployGameDirectory=<dir>` to deploy somewhere else, such as a scratch folder when testing the build files.
- Building copies `mod/` into `out/` and deletes files from `out/` that are no longer in `mod/`, except `Assemblies`.
- Building checks `mod/About/About.xml` (target `CheckAboutXml` in `Directory.Build.targets`) and fails with `ORDO001` errors naming the line. The rules are in the repo README's Conventions.
- `shared/` holds C# compiled into every mod as internal types (Rider shows them under a `shared` folder in each project). `shared/Harmony` (`HarmonyPatching.TryPatch`) is compiled only into mods whose About.xml mentions `brrainz.harmony`, so a mod without Harmony never references it. A change there rebuilds every mod that includes it.
- Other mods' DLLs come in as `<WorkshopReference Include="<AssemblyName>" WorkshopId="<id>" />`, resolved before compiling to the 1.7 `Assemblies` folder, or the 1.6 one. A missing Workshop mod fails the build with its ID.
- In Debug, `build/LocalRimWorldReferences.targets` swaps each assembly `Krafs.Rimworld.Ref` provides for the file of the same name in the install root, when the version in `Version.txt` matches the package's. Rider then shows real method bodies. The build prints "Local RimWorld referenced: true (69 assemblies ...)". Only the package's names are swapped, because the root also holds the .NET runtime's DLLs. Release builds, and any game version that doesn't match, compile against the package. Both produce the same assembly references (checked on 2026-11-21). Pass `-p:EnableLocalRimWorldReferences=false` to turn the swap off.
- `RimWorldDirectory` has no trailing slash, so write `$(RimWorldDirectory)\Mods`, not `$(RimWorldDirectory)Mods`.
- `Krafs.Rimworld.Ref` is pinned in `Directory.Build.props` (`1.7.5014-unstable`), so every project compiles against the same game build. When the game updates, raise it to the package version matching the build number in the game's `Version.txt` (the build prints both as "RimWorld version" and "Krafs.Rimworld.Ref version").
- BridgeTools sets `IsRimWorldMod` false (no About.xml check, no `mod/` copy) and a `DeployTarget` of its own. It references every mod project through a glob, so a new mod needs no change there.
- Mods compile against a publicized `Assembly-CSharp` (`Krafs.Publicizer`), so protected and private game members are reachable. Overrides of protected methods must then be declared `public override`.

## Checking a deploy

- **Stale DLLs:** restoring files with `git` or copying files with old timestamps can make `dotnet build` skip compiling and deploy the old DLL. Use `-t:Rebuild` when in doubt.
- Check the deployed DLL, not the source: search it for a new class or method name, or compare hashes with `out/Assemblies`.
- `ordo/runtime_info` reports the assembly the game actually loaded, with its path.

## Reading the game's code

- **DecompilerServer** (pardeike, v1.5.0 since 2026-11-28; `~\Tools\decompiler-server\decompilerserver-v1.5.0-win-x64\DecompilerServer.exe`) is a user-scope MCP server, `decompiler`, with its `decompiler-mcp` skill in `~\.claude\skills`. Use it first for reading members of the game or a mod (`load_assembly`, `search_symbols`, `get_decompiled_source`), call questions (`find_callers`, `find_usages`, `find_callees`), IL before writing a transpiler (`get_il`), string literals in loaded assemblies, and comparing two versions of a DLL under aliases (`compare_contexts`, `compare_symbols` with `compareMode: "body"`), such as a game update, a RimBridgeServer update, or two builds of the same mod. Its tools load at session start. Keep ilspycmd (below) for a whole decompiled source tree, and a byte search over the Workshop folder to find which of hundreds of mods' DLLs holds a string.
- ilspycmd is installed globally since 2026-11-23, at `~\.dotnet\tools\ilspycmd.cmd` (a `.cmd`, so run it from PowerShell, not Git Bash). Run `ilspycmd.cmd -p -o <out> "D:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll"`, with `<out>` in the scratchpad. Scratchpad output doesn't survive between sessions, so expect to regenerate it.
- Other mods' DLLs decompile the same way, with `-r "D:\SteamLibrary\steamapps\common\RimWorld"` so it resolves the game's types: Workshop mods are under `D:\SteamLibrary\steamapps\workshop\content\294100\<id>`. MultiFloors (3384660931) has no public source repo (checked 2026-11-23), so its decompiled `1.6\Assemblies\MultiFloors.dll` is the reference; its `PrepatcherFields` lists the fields it injects and `HarmonyPatch_ExposePrepatcherFields` which of them are saved.
- Waylith's own engine code is in `Waylith.dll`, and its launcher is in `RimWorld.dll`. When the user opens one in Rider's Assembly Explorer, the decompiled types land in `%APPDATA%\JetBrains\Rider2026.2\resharper-host\DecompilerCache`, where they can be read. Rider's MCP tools can't reach them directly.

## Conventions

The repo's rules are in the Conventions section of the root `README.md`: names, project files, About.xml, Harmony, other mods, code and XML. Read it before adding a mod or a dependency. The About.xml rules fail the build; nothing else checks the others. A few that matter for the tooling here:

- Run `dotnet format whitespace <project>` after writing C#; it fixes indentation and braces but doesn't wrap parameters.
- Package IDs are `Ordo.<Mod>`. The bridge tools treat any running mod with code whose packageId starts with `Ordo.` as "ours".
- A mod's `.csproj` holds only `WorkshopReference` items (and comments on them). Put any build setting in the root files instead.
