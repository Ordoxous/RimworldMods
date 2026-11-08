using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

// Gives its abilities, and the trainables those abilities require, to every member race
public class AnimalTagDef : Def
{
	public List<AbilityDef> abilities = new();

	public List<ThingDef> races = new();

	public override IEnumerable<string> ConfigErrors()
	{
		foreach (var error in base.ConfigErrors())
		{
			yield return error;
		}

		foreach (var race in races)
		{
			if (race.race == null)
			{
				yield return $"{race.defName} is not a pawn race";
			}
		}
	}
}

public static class AnimalTagUtility
{
	public static void ApplyAllTags()
	{
		var kindsByRace = DefDatabase<PawnKindDef>.AllDefsListForReading.ToLookup(kind => kind.race);

		foreach (var tag in DefDatabase<AnimalTagDef>.AllDefsListForReading)
		{
			foreach (var race in tag.races)
			{
				foreach (var ability in tag.abilities)
				{
					foreach (var kind in kindsByRace[race])
					{
						kind.abilities ??= new List<AbilityDef>();
						if (!kind.abilities.Contains(ability))
						{
							kind.abilities.Add(ability);
						}
					}

					foreach (var requiresTrainable in ability.comps.OfType<CompProperties_AbilityRequiresTrainable>())
					{
						race.race.specialTrainables ??= new List<TrainableDef>();
						if (!race.race.specialTrainables.Contains(requiresTrainable.trainableDef))
						{
							race.race.specialTrainables.Add(requiresTrainable.trainableDef);
						}
					}
				}
			}
		}
	}
}
