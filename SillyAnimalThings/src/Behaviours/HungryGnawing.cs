using RimWorld;
using Verse;
using Verse.AI;

namespace Ordo.SillyAnimalThings;

[DefOf]
public static class SillyAnimalThingsDefOf
{
	public static AbilityDef Ordo_Gnaw;

	public static AbilityDef Ordo_Regenerate;

	public static AbilityDef Ordo_HiddenDragon;

	static SillyAnimalThingsDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(SillyAnimalThingsDefOf));
}

// Hungry animals that can gnaw, and aren't the player's, eat nearby wooden buildings before looking for other food
public static class HungryGnawing
{
	private const float SearchRadius = 12f;

	public static bool Prefix_JobGiver_GetFood_TryGiveJob(
		JobGiver_GetFood __instance,
		Pawn pawn,
		ref Job __result
	)
	{
		if (pawn.Faction == Faction.OfPlayer || !pawn.Spawned)
		{
			return true;
		}

		var gnaw = pawn.abilities?.GetAbility(SillyAnimalThingsDefOf.Ordo_Gnaw);
		if (gnaw == null || !gnaw.CanCast)
		{
			return true;
		}

		var food = pawn.needs?.food;
		if (food == null || food.CurCategory < __instance.minCategory || food.CurLevelPercentage > __instance.maxLevelPercentage)
		{
			return true;
		}

		var building = GenClosest.ClosestThingReachable(
			pawn.Position,
			pawn.Map,
			ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
			PathEndMode.Touch,
			TraverseParms.For(pawn),
			SearchRadius,
			thing => CompAbilityEffect_Gnaw.IsWooden(thing) && !thing.IsBurning() && pawn.CanReserve(thing)
		);
		if (building == null)
		{
			return true;
		}

		__result = gnaw.GetJob(building, building);
		return false;
	}
}
