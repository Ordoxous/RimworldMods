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
- Other mods' DLLs come in as `<WorkshopReference Include="<AssemblyName>" WorkshopId="<id>" />`, resolved before compiling to the 1.7 `Assemblies` folder, or the 1.6 one. A missing Workshop mod fails the build with its ID.
- `build/LocalRimWorldReferences.targets` swaps in the real game assemblies when their version matches `Krafs.Rimworld.Ref`, so Rider shows real method bodies. It looks for them in `*/Managed`, which the 1.7 Waylith build doesn't have, so for now it always prints "Local RimWorld referenced: false" and compiles against the package.
- `Krafs.Rimworld.Ref` is pinned in `Directory.Build.props` (`1.7.5014-unstable`), so every project compiles against the same game build. When the game updates, raise it to the package version matching the build number in the game's `Version.txt` (the build prints both as "RimWorld version" and "Krafs.Rimworld.Ref version").
- BridgeTools sets `IsRimWorldMod` false (no About.xml check, no `mod/` copy) and a `DeployTarget` of its own. It references every mod project through a glob, so a new mod needs no change there.
- Mods compile against a publicized `Assembly-CSharp` (`Krafs.Publicizer`), so protected and private game members are reachable. Overrides of protected methods must then be declared `public override`.

## Checking a deploy

- **Stale DLLs:** restoring files with `git` or copying files with old timestamps can make `dotnet build` skip compiling and deploy the old DLL. Use `-t:Rebuild` when in doubt.
- Check the deployed DLL, not the source: search it for a new class or method name, or compare hashes with `out/Assemblies`.
- `ordo/runtime_info` reports the assembly the game actually loaded, with its path.

## Reading the game's code

- Install ilspycmd somewhere outside the repo, such as the scratchpad: `dotnet tool install ilspycmd --tool-path <dir>`. Then run `<dir>\ilspycmd.cmd -p -o <out> "D:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll"`. Scratchpad copies don't survive between sessions, so expect to regenerate it.
- Waylith's own engine code is in `Waylith.dll`, and its launcher is in `RimWorld.dll`. When the user opens one in Rider's Assembly Explorer, the decompiled types land in `%APPDATA%\JetBrains\Rider2026.2\resharper-host\DecompilerCache`, where they can be read. Rider's MCP tools can't reach them directly.

## Conventions

The repo's rules are in the Conventions section of the root `README.md`: names, project files, About.xml, Harmony, other mods, code and XML. Read it before adding a mod or a dependency. The About.xml rules fail the build; nothing else checks the others. A few that matter for the tooling here:

- Run `dotnet format whitespace <project>` after writing C#; it fixes indentation and braces but doesn't wrap parameters.
- Package IDs are `Ordo.<Mod>`. The bridge tools treat any running mod with code whose packageId starts with `Ordo.` as "ours".
- A mod's `.csproj` holds only `WorkshopReference` items (and comments on them). Put any build setting in the root files instead.
