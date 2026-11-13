using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public static class HostilityUtility
{
	// How long an animal keeps treating the pawn it cast at as an enemy: long enough to walk up to it, cast and land
	private const int FoeMemoryTicks = 900;

	private static readonly ConditionalWeakTable<Pawn, FoeRecord> Foes = new();

	// Thing.HostileTo(Thing) treats a player's roaming animal as peaceful until it has recently fought, which would stop
	// it hurting or scaring anyone with an ability, so this also checks against the caster's faction.
	//
	// It also counts whoever the caster is fighting without being hostile to them: a wild animal isn't hostile to the
	// colonist hitting it, or to the prey it's hunting, but its abilities should still land on them.
	public static bool IsEnemyOf(
		this Thing thing,
		Pawn caster
	)
	{
		if (thing == null || thing == caster)
		{
			return false;
		}

		if (thing.HostileTo(caster) || thing.HostileTo(caster.Faction))
		{
			return true;
		}

		if (caster.mindState?.meleeThreat == thing)
		{
			return true;
		}

		return Foes.TryGetValue(caster, out var record)
		       && record.Foe == thing
		       && Find.TickManager.TicksGame - record.Tick <= FoeMemoryTicks;
	}

	// Same faction, or for wild animals, which have no faction, the same race
	public static bool IsAllyOf(
		this Pawn other,
		Pawn caster
	)
	{
		if (other == caster || other.IsEnemyOf(caster))
		{
			return false;
		}

		return caster.Faction != null
			? other.Faction == caster.Faction
			: other.Faction == null && other.def == caster.def;
	}

	// Called when the AI picks an ability to use on a foe
	public static void RememberFoe(
		Pawn caster,
		Thing foe
	)
	{
		var record = Foes.GetOrCreateValue(caster);
		record.Foe = foe;
		record.Tick = Find.TickManager.TicksGame;
	}

	private sealed class FoeRecord
	{
		public Thing Foe;

		public int Tick;
	}
}
