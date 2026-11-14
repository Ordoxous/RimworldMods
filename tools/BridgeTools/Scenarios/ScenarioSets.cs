using System;
using System.Collections.Generic;

namespace Ordo.BridgeTools.Scenarios;

// Every mod's scenarios, by the packageId of the mod they test. ordo/run_scenarios runs the sets of every running mod, or the
// one passed as mod. To test another mod, write its scenarios in a file like SillyAnimalThingsScenarios.cs, add a line here, and
// a ProjectReference with Private="false" to BridgeTools.csproj. Mods that aren't loaded are fine: their scenarios never run, so
// their types are never needed.
public static class ScenarioSets
{
	public static readonly (string PackageId, Func<List<Scenario>> Scenarios)[] All =
	{
		("Ordo.SillyAnimalThings", () => SillyAnimalThingsScenarios.All),
		("Ordo.MechUpgradesUi", () => MechUpgradesUiScenarios.All),
		("Ordo.BlueprintMaterials", () => BlueprintMaterialsScenarios.All),
		("Ordo.LegendaryIsUnique", () => LegendaryIsUniqueScenarios.All)
	};
}
