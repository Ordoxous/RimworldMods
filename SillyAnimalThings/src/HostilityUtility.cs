using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public static class HostilityUtility
{
	// Thing.HostileTo(Thing) treats a player's roaming animal as peaceful until it has recently fought, which would stop
	// it hurting or scaring anyone with an ability, so this also checks against the caster's faction
	public static bool IsEnemyOf(
		this Thing thing,
		Pawn caster
	)
	{
		return thing.HostileTo(caster) || thing.HostileTo(caster.Faction);
	}
}
