using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class ProjectileHediffExtension : DefModExtension
{
	public HediffDef hediffDef;
}

// Gives the flesh pawn it hits the hediff named by its ProjectileHediffExtension, and does no damage, so the hit doesn't
// flash as an injury. The def's landedEffecter shows the impact.
public class Projectile_ApplyHediff : Projectile
{
	public override void Impact(
		Thing hitThing,
		bool blockedByShield = false
	)
	{
		base.Impact(hitThing, blockedByShield);

		var hediffDef = def.GetModExtension<ProjectileHediffExtension>()?.hediffDef;
		if (hediffDef != null && !blockedByShield && hitThing is Pawn { Dead: false } pawn && pawn.RaceProps.IsFlesh)
		{
			pawn.health.AddHediff(hediffDef);
		}
	}
}
