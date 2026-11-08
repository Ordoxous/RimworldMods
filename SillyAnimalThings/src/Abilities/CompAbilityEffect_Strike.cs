using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class CompProperties_AbilityStrike : CompProperties_AbilityEffect
{
	public DamageDef damageDef;

	public float damageBase;

	public float damagePerBodySize;

	public float armorPenetration;

	// Above zero, the strike also staggers the target for this long and knocks it back
	public int knockbackStaggerTicks;

	// How far away a hostile can be before a wild or enemy caster goes after it
	public float aiMaxDistance = 8f;

	public CompProperties_AbilityStrike()
	{
		compClass = typeof(CompAbilityEffect_Strike);
	}
}

// A single melee hit whose damage scales with the caster, so one ability suits both rats and megaspiders
public class CompAbilityEffect_Strike : CompAbilityEffect
{
	public new CompProperties_AbilityStrike Props => (CompProperties_AbilityStrike)props;

	public override void Apply(
		LocalTargetInfo target,
		LocalTargetInfo dest
	)
	{
		base.Apply(target, dest);

		var pawn = parent.pawn;
		if (target.Pawn is not { Spawned: true } targetPawn)
		{
			return;
		}

		var damage = Props.damageBase + Props.damagePerBodySize * pawn.BodySize;
		targetPawn.TakeDamage(new DamageInfo(Props.damageDef, damage, Props.armorPenetration, instigator: pawn));

		if (Props.knockbackStaggerTicks > 0 && targetPawn.Spawned && !targetPawn.Dead)
		{
			KnockbackUtility.KnockBack(targetPawn, pawn.Position, pawn, Props.knockbackStaggerTicks);
		}
	}

	public override bool AICanTargetNow(
		LocalTargetInfo target
	)
	{
		var pawn = parent.pawn;
		if (pawn.Faction == Faction.OfPlayer || target.Pawn is not { } targetPawn)
		{
			return false;
		}

		return !targetPawn.Downed
		       && targetPawn.HostileTo(pawn)
		       && targetPawn.Position.InHorDistOf(pawn.Position, Props.aiMaxDistance);
	}
}
