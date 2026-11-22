# Saddlebound ideas

Ideas not yet planned or built. The README holds the design, the plan and the open questions; an idea moves there once it's taken on. Ideas are grouped by area, smallest first. After each name come the effort, S (an hour or two), M (about a day) or L (more); "README" if the README already raises it; and any DLC or mod it needs. Discarded ideas stay listed with why they were dropped, so they aren't raised again.

## Ideas

### Memes, precepts and thoughts

- **On-foot penalty** (S, README). Mounted Glory's "fought on foot with a mount available" thought.
- **VME exclusion** (S, README, VME). Horde Riders excludes VME's city-builder meme when VME is loaded.
- **Mount Ownership stacking** (S, README, Mount Ownership). Lower Essential's +3, or add a profile with Mount Ownership to judge how it stacks with that mod's +1/+4.
- **Mount funeral** (M–L). A ritual for a fallen mount that eases "our mount died" when it goes well, as vanilla's funeral does for a colonist. It belongs with Mount Death: Mourned. It needs an obligation started by `Ordo_MountDied`, a grave holding an animal corpse as its target, and its own outcome defs. The README left out the Naming of the Steed ritual for its effort, so weigh this the same way.
- **Version 2** (L, README). The Saddle Lord role, Eating Mounts, the Steed Kinship meme, Mount Training: Revered and Stabling: Desired.

### Mounts and taming

- **Taming preferred species** (S). A small bonus to taming and training the ideoligion's preferred mounts. A narrower take on Version 2's Mount Training: Revered (+10% on any mountable animal), so build one or the other.
- **Mount sellers** (M). A trader kind or quest from Horde Riders factions offering trained mounts of their preferred species. A stock generator is given the faction, so it can read the faction's ideoligion. The hard part is making the stock trained enough for Giddy-Up's checks.

### Raids and visitors

- **Mounted traders** (S). Horde Riders trade caravans, travelers and visitors arrive mounted too, not only raiders. Giddy-Up's Caravans module already calls `GenerateMounts` for those incidents and reads `visitorMountChance` / `visitorMountChancePreInd` for non-hostile factions, so the Horde prefix only has to set those two as well. It works only with Caravans on, and the setting would read better as "Horde mounts".
- **Preferred mounts in raids** (M, README). Weight Giddy-Up's mount pick toward the raiders' preferred species. `GenerateMounts` picks from three lists (local, foreign, domestic) with two weight rules, the domestic one an inline lambda, and a preferred species must still pass its filters (mountable, within the raid's points, worthy of the rider, in season for wild lists). Either replace both `TryRandomElementByWeight` calls with a wrapper that boosts preferred species, or have the prefix sometimes pick one itself.

### Upkeep

- **Icons** (S, README). Real icons for the meme and the three issues, and one for the preferred-mounts issue, which has none.
- **The planned tests** (M, README). Caravan riding counts as riding, the tracker survives save and load, and Horde Riders raids come mounted. The caravan test has to form a real world caravan with paired mounts, the save test saves and reloads inside a scenario, and the raid test generates many full raids. A showcase clip of a mounted Horde Riders raid goes with them.

## Discarded ideas

- **Caravan thoughts** (S). Riding: Essential pawns pleased by a fully mounted caravan and unhappy walking in one. Dropped because Essential already covers it: riding in a caravan with a paired mount counts as riding (+3), and walking with a free mount gets the usual penalty.
