# Saddlebound handoff: Ideology memes for Giddy-Up

This is the starting brief for a new mod, written 2026-11-14 from the design pass on the `ideas.txt` line "Memes for GiddyUp". Nothing is built yet. This folder holds only this file. Once the mod exists, replace this file with the mod's `README.md` and keep the design decisions that still hold.

## Goal

A separate mod that adds Ideology memes, precepts and a role built around riding with Giddy-Up. Riding becomes a cultural practice: how often pawns ride, fighting from the saddle, and how mounts are treated. Other mods already cover owning a personal mount and horse-specific bonuses (see [Overlap](#overlap-with-installed-mods)), so this mod leaves those out.

- **Name:** Saddlebound. Alternative: RidingCreeds.
- **Package ID:** `Ordo.Saddlebound`
- **Folder, project, assembly:** `Saddlebound`, following the repo layout in the root `README.md`.

## Before starting

- **Load the `rimworld-mod-dev` skill.** Then read `references/new-mod-setup.md`: it covers the csproj, GABS profiles, the scenario suite and a showcase for a new mod.
- **Pattern to follow:** Tweaks reaches optional mods by reflection, and its scenarios live in `tools/BridgeTools/Scenarios/TweaksScenarios.cs`.
- **First manual check:** confirm Giddy-Up still matches the API table below. It's a community fork that updates often.

## Dependencies

| Kind | What |
|---|---|
| Hard | Harmony, Ideology, **Giddy-Up 2 - Continued** (`MemeGoddess.GiddyUp`, workshop `3674332861`, v2.2.5 when checked, supports 1.4–1.7) |
| Soft (`MayRequire`) | Mount Ownership (`font.mountownership`), Alpha Memes, Vanilla Memes Expanded |

- **Giddy-Up is all-in-one.** Core, Ride and Roll, Battle Mounts and Caravans ship in one mod, and each is switched on in its settings. Its only dependency is Harmony.
- **Source:** github.com/MemeGoddess/GiddyUp2. A copy ships next to the DLL in the workshop folder.
- **Reference `GiddyUpCore.dll` directly,** since it's a hard dependency: `HintPath` `$(WorkshopDirectory)\3674332861\1.7\Assemblies\GiddyUpCore.dll`, `Private=false`.
- **Keep every Giddy-Up call in one `src/GiddyUpAdapter.cs`,** so an API change breaks one file.

## Giddy-Up API (namespace `GiddyUp`, as read on 2026-11-14)

| Need | Hook |
|---|---|
| Is this pawn mounted? | `StorageUtility.IsMounted(this Thing)`. It reads the static `ExtendedDataStorage.isMounted` `HashSet<int>`, so it's cheap. |
| Rider and mount data | `pawn.GetExtendedPawnData()` returns `ExtendedPawnData`, with `Mount`, `Rider`, `ReservedMount`, `ReservedBy`, `canRide`, `automount` and `selectedForCaravan`. |
| Is this animal carrying someone? | `IsMountableUtility.IsMountedAnimal(animal, out rider)`, `IsEverMountable`, `IsAllowed(rider, animal)` |
| Mount and dismount events | `MountUtility.Mount(rider, animal)` is private. `Dismount(...)` and `InvoluntaryDismount(...)` are public. **Simplest single hook:** the public `ExtendedPawnData.Mount` setter (non-null means mounting, null means dismounting). **Unverified:** whether `Dismount` clears the mount through the setter or writes the field directly. |
| Mounted combat | Postfix on `VerbProperties.AdjustedAccuracy`, `StatPart_Riding` (speed), `StatPart_Armor`, and the `CustomStats` def extension (`speedModifier`, `armorModifier`) |
| Mounted raids | `MountUtility.GenerateMounts(ref List<Pawn>, IncidentParms)`, using the `mountChance` setting (default 40) and `battleMountsEnabled` |
| Caravans | Riding is chosen per pawn with `selectedForCaravan`. Patches are in `Caravan/Harmony/*`. |
| Jobs allowed while mounted | `GiddyUp.CanDoMounted` mod extension on a `JobDef` |
| Module switches | `ModSettings_GiddyUp.battleMountsEnabled`, `caravansEnabled` |

## Overlap with installed mods

- **Mount Ownership** (`font.mountownership`, lists 1.6 only):
  - A "mount ownership" issue with Important and Essential precepts: −1 to −5 mood without a personal mount, +3 to +6 with one.
  - Riding thoughts: +1, or +4 for a named mount.
  - A joy job for riding for leisure.
  - **Don't repeat personal-mount ownership or naming.**
- **Alpha Memes, `AM_Horses`** (has a 1.7 build): a horses-only precept with starting horses, handling, speed, +25% caravan riding speed and a horse-death thought.
  - **Don't repeat the caravan speed bonus.**
- **Vanilla Memes Expanded:** the `VME_Nomad` meme and `VME_Ranching_Nomadic` precept are travel flavour with no riding logic.
  - When it's loaded, add this mod's Riding precept to `VME_Nomad`'s suggested precepts, and to `AM_Horses`'s when Alpha Memes is loaded.
- **Web search:** found no other riding meme or precept mod for Giddy-Up.

## Design

### Memes

1. **Horde Riders** (Misc group). "The saddle is our home; to walk is to be lesser."
   - Requires Riding: Essential. Adds Mounted Glory and the Saddle Lord role.
   - Factions with this meme raid mostly mounted (P6).
   - Excluded with VME's city-builder tag when VME is loaded.
2. **Steed Kinship.** "Our mounts are kin, not livestock."
   - Requires Mount Death: Mourned and Eating Mounts: Abhorrent.
   - Pairs well with Horde Riders but doesn't depend on it. Planned for version 2.
3. *(Optional)* **Wandering Saddle.** Only worth adding if VME isn't wanted. Otherwise patch `VME_Nomad`.

### Precepts

| # | Issue → precepts | Effect (mood) | How it's built |
|---|---|---|---|
| P1 | **Riding**: Disliked / Respected / Essential | Disliked: "had to ride" −2 (memory, 1 day). Respected: "rode recently" +2 (situational, last 24h). Essential: +3 within 24h, −2 after 2 days without, −5 after 4 days. | A postfix on the `ExtendedPawnData.Mount` setter records each pawn's last riding tick in a saved `GameComponent`. Thought workers subclass `ThoughtWorker_Precept`. A caravan pawn with `selectedForCaravan` counts as riding. |
| P2 | **Mount Death**: Mourned / Don't care | Mourned: "our mount died" −4 for everyone, −8 for its usual rider | A postfix on `Pawn.Kill` for player-faction animals the tracker has seen ridden fires a `HistoryEventDef` `Saddlebound_MountDied`. The precepts use `PreceptComp_KnowsMemoryThought`, so that side is all XML. |
| P3 | **Eating Mounts**: Abhorrent / Horrible / Disapproved / Acceptable | Same ladder as vanilla's cannibalism-style precepts (−8/−5/−2) | A custom event fired from postfixes on slaughtering, and on eating meat whose race `IsEverMountable`. Needs a small `PreceptComp` filter. Covers modded animals automatically. |
| P4 | **Mounted Glory**: Glorious / Normal | "Killed an enemy from the saddle" +3 memory (stacks 3). "Fought on foot with a mount available" −1 while drafted and unmounted with a free mount. | A `Pawn.Kill` postfix when the attacker `IsMounted()` fires an event (XML comps). The foot penalty is a situational C# worker. |
| P5 | **Mount Training**: Revered / Normal | Taming or training a mountable animal gives "bonded with a steed" +2. Handlers get +10% tame and train chance. | XML `statOffsets` on the precept, plus a postfix on `InteractionWorker_RecruitAttempt` and on training for the memory. |
| P6 | **Horde Raids** (meme effect, not an issue) | Factions with Horde Riders raid with about 90% of raiders mounted | A prefix on `MountUtility.GenerateMounts` checks `parms.faction.ideos.PrimaryIdeo.HasMeme` and raises the chance for that call. |
| P7 | **Stabling**: Desired | "Mounts kept in a roofed pen or barn" +1, "Mounts sleep under the open sky" −1 | A situational worker sampling pen markers. Skip in version 1. |

### Role: Saddle Lord (specialist)

- **Requirement:** Animals 6+. Prefers a pawn with a bonded mount.
- **Stats:**
  - +15% move speed while mounted: a `StatPart` checking the role and `IsMounted`.
  - +10% mounted ranged accuracy: a postfix that runs after Giddy-Up's `AdjustedAccuracy`. Check that the two stack sensibly.
- **Ability: Rallying Charge.** Nearby mounted allies get a speed and melee-hit hediff for 20s. Built on vanilla `CompAbilityEffect_GiveHediff` plus a small C# filter so only mounted pawns are affected.
- **Inactive while unmounted,** using vanilla's role-inactive rule.

### Left out

- **Naming of the Steed ritual:** about 40% of the mod's effort, and it overlaps Mount Ownership's naming. Leave it out unless the ritual itself becomes the point.
- **Styles:** art-heavy, and they add little to gameplay.

## Ranking and scope

| Rank | Item | Fun | Effort |
|---|---|---|---|
| 1 | P1 Riding + Horde Riders meme | High | Low–Med |
| 2 | P6 Horde Raids | High | Low |
| 3 | P2 Mount Death | Med | Low |
| 4 | P4 Mounted Glory | High | Med |
| 5 | Saddle Lord role | High | Med |
| 6 | P3 Eating Mounts | Med | Med |
| 7 | P5 Mount Training | Low–Med | Low |
| 8 | P7 Stabling | Low | Med |
| 9 | Naming ritual | Med | High |

- **Version 1:** P1, the Horde Riders meme, P6, P2, and P4's kill memory.
- **Version 2:** the Saddle Lord role, P3, the Steed Kinship meme, P5.

## Risks

- **API drift:** the private `Mount`, the static `isMounted` set and `ExtendedPawnData`'s layout can all change. Keep calls in the adapter, and add a `setup_giddyup_api` scenario that checks every member by reflection.
- **Saves:** `isMounted` is rebuilt on load. The tracker stores ticks, not live state, so the order things load in doesn't matter. If the save code writes `_mount` directly, the setter hook won't fire on load, which is fine.
- **Caravans:** pawns in a world caravan aren't "mounted". Count `selectedForCaravan` as riding, or Essential followers get mood penalties while travelling.
- **Nagging:** P1 Essential and P4's foot penalty would annoy where mounting is blocked (`AreaNoMount`) or there's no mountable animal. Suppress them when the colony has no mount the pawn `IsAllowed` to ride.
- **Giddy-Up settings:** when Battle Mounts or Caravans are switched off, P6 and the caravan rule must do nothing.
- **Stacking:** next to Mount Ownership's +1/+4 riding thoughts, P1's +3 may stack too high. Cap or lower it when Mount Ownership is loaded.
- **Balance:** the Essential ladder (−5 after 4 days) is in line with vanilla's Essential precepts. Check it in play.

## Test scenarios

These use the repo's BridgeTools pattern, in a `SaddleboundScenarios` class:

- **`setup_no_load_errors`, `setup_giddyup_api`:** every adapter member resolves.
- **`riding_respected_thought`:** a colonist with Riding: Respected and a tame horse mounts, ticks advance, and they have "rode recently". After dismounting and one more day, it's gone.
- **`riding_essential_penalty`, `riding_essential_suppressed_no_mounts`:** the penalty appears only when a mount the pawn is allowed to ride exists.
- **`mount_death_memory`:** a mount that's been ridden is killed. Everyone gets the memory, and its rider gets the stronger one.
- **`mounted_kill_glory`:** a mounted kill gives the memory. An unmounted kill doesn't.
- **`horde_raid_mounted`:** a Horde Riders faction raids about 90% mounted. A faction without the meme stays near Giddy-Up's setting.
- **`caravan_counts_as_riding`:** a `selectedForCaravan` pawn in a world caravan gets no Essential penalty.
- **`save_load_tracker`:** the tracker's ticks survive a save and reload.
- **Showcase clip:** a mounted Horde Riders raid hitting a colony.

Each needs a GABS profile with Ideology and Giddy-Up, plus a `-headless` twin. Add a second profile with Mount Ownership for the stacking cap.

## Open questions for the user

- The name: Saddlebound, or something else?
- Should Horde Raids (P6) be its own setting? It changes every raid from those factions.
- Keep Steed Kinship and the Eating Mounts ladder in scope, or stay a riding-only mod?
