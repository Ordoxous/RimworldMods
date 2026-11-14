using System;
using System.Collections.Generic;

namespace Ordo.BridgeTools.Showcase;

// Every mod's showcase clips, by the packageId of the mod they show. ordo/record_showcase records one mod's clips at a time.
// To showcase another mod, write its clips in a file like SillyAnimalThingsShowcase.cs and add a line here.
public static class ShowcaseSets
{
	public static readonly (string PackageId, Func<List<ShowcaseClip>> Clips)[] All =
	{
		("Ordo.SillyAnimalThings", () => SillyAnimalThingsShowcase.All),
		("Ordo.MechUpgradesUi", () => MechUpgradesUiShowcase.All),
		("Ordo.BlueprintMaterials", () => BlueprintMaterialsShowcase.All)
	};
}
