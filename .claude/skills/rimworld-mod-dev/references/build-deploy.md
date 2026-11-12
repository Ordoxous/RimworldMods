# Building and deploying

## Paths

- RimWorld: `D:\SteamLibrary\steamapps\common\RimWorld` (1.7, game data under `Data\`, `Assembly-CSharp.dll` in the install root). `Directory.Build.props` sets `SteamLibraryDirectory` to `D:\SteamLibrary`; override it in a git-ignored `Directory.Build.local.props` on another machine.
- `dotnet` may not be on `PATH`: use `~/.dotnet/dotnet.exe`. If a build step prints nothing at all, check that it actually ran.
- Mods deploy to `RimWorld\Mods\<ProjectName>`. BridgeTools deploys to `RimWorld\BridgeTools\Ordo` (companions load from there, not from `Mods`).

## Building

```
dotnet build <Mod>/<Mod>.csproj -c Debug
dotnet build <Mod>/<Mod>.csproj -c Debug -t:CopyToRimworld
```

- **Always `-c Debug`.** `Directory.Build.props` is read before the SDK sets the default configuration, so without `-c Debug` its Debug group doesn't apply. The build then goes to `bin/Debug` instead of `out/Assemblies`, and `CopyToRimworld` deploys whatever was in `out/` already, often an older DLL from one of Rider's background builds. The symptom in game is "Could not find a type named Ordo..." for anything new.
- **Build and copy as two commands.** `CopyToRimworld` only copies `out/`; it doesn't build.
- The Debug build embeds its PDB and isn't optimized, which is what hot reload and the debugger need.
- With the installed game's version matching `Krafs.Rimworld.Ref`, `Directory.Build.targets` swaps in the real game assemblies ("Local RimWorld referenced: true"), so Rider shows real method bodies.
- Mods compile against a publicized `Assembly-CSharp` (`Krafs.Publicizer`), so protected and private game members are reachable. Overrides of protected methods must then be declared `public override`.

## Checking a deploy

- **Stale DLLs:** restoring files with `git` or copying files with old timestamps can make `dotnet build` skip compiling and deploy the old DLL. Use `-t:Rebuild` when in doubt.
- Check the deployed DLL, not the source: search it for a new class or method name, or compare hashes with `out/Assemblies`.
- `ordo/runtime_info` reports the assembly the game actually loaded, with its path.

## Reading the game's code

- Install ilspycmd somewhere outside the repo, such as the scratchpad: `dotnet tool install ilspycmd --tool-path <dir>`. Then run `<dir>\ilspycmd.cmd -p -o <out> "D:\SteamLibrary\steamapps\common\RimWorld\Assembly-CSharp.dll"`. Scratchpad copies don't survive between sessions, so expect to regenerate it.
- Waylith's own engine code is in `Waylith.dll`, and its launcher is in `RimWorld.dll`. When the user opens one in Rider's Assembly Explorer, the decompiled types land in `%APPDATA%\JetBrains\Rider2026.2\resharper-host\DecompilerCache`, where they can be read. Rider's MCP tools can't reach them directly.

## Conventions

- C#: tabs, a file-scoped namespace `Ordo.<Mod>`, and one parameter per line in method signatures.
- XML: 2 spaces. DefNames use the `Ordo_` prefix. Guard DLC content with `MayRequire="Ludeon.RimWorld.<DLC>"`.
- Package IDs are `Ordo.<Mod>`. The bridge tools treat any running mod with code whose packageId starts with `Ordo.` as "ours".
