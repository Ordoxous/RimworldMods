using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class DashExtension : DefModExtension
{
	// A pawn flyer with no height, so the pawn moves along the ground instead of jumping
	public ThingDef flyerDef;
}

// A jump that uses the flyer named by the ability's DashExtension
public class Verb_CastAbilityDash : Verb_CastAbilityJump
{
	public override ThingDef JumpFlyerDef => ability?.def.GetModExtension<DashExtension>()?.flyerDef ?? base.JumpFlyerDef;
}
