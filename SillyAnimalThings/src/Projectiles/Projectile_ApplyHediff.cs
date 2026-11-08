using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class ProjectileHediffExtension : DefModExtension
{
	public HediffDef hediffDef;
}

// A bullet that also gives the flesh pawn it hits the hediff named by its ProjectileHediffExtension
public class Projectile_ApplyHediff : Bullet
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
