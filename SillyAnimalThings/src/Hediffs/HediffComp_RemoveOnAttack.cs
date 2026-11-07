using Verse;

namespace Ordo.SillyAnimalThings;

public class HediffCompProperties_RemoveOnAttack : HediffCompProperties
{
	public HediffCompProperties_RemoveOnAttack()
	{
		compClass = typeof(HediffComp_RemoveOnAttack);
	}
}

// Ends the hediff once the pawn uses a violent verb, so an ambush only stays hidden until it strikes
public class HediffComp_RemoveOnAttack : HediffComp
{
	private bool _attacked;

	public override bool CompShouldRemove => _attacked;

	public override void Notify_PawnUsedVerb(
		Verb verb,
		LocalTargetInfo target
	)
	{
		if (verb.verbProps.violent)
		{
			_attacked = true;
		}
	}

	public override void CompExposeData()
	{
		Scribe_Values.Look(ref _attacked, "attacked");
	}
}
