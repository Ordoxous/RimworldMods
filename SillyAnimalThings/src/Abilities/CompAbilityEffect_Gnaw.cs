using System.Linq;
using RimWorld;
using Verse;

namespace Ordo.SillyAnimalThings;

public class CompProperties_AbilityGnaw : CompProperties_AbilityEffect
{
	public float damage = 80f;

	// Fraction of the food need restored per gnaw
	public float nutritionGain = 0.25f;

	public CompProperties_AbilityGnaw()
	{
		compClass = typeof(CompAbilityEffect_Gnaw);
	}
}

public class CompAbilityEffect_Gnaw : CompAbilityEffect
{
	public new CompProperties_AbilityGnaw Props => (CompProperties_AbilityGnaw)props;

	public static bool IsWooden(
		Thing thing
	)
	{
		if (thing is not Building building || !building.def.useHitPoints)
		{
			return false;
		}

		if (building.Stuff != null)
		{
			return building.Stuff.stuffProps?.categories?.Contains(StuffCategoryDefOf.Woody) == true;
		}

		return building.def.costList?.Any(cost => cost.thingDef == ThingDefOf.WoodLog) == true;
	}

	public override void Apply(
		LocalTargetInfo target,
		LocalTargetInfo dest
	)
	{
		base.Apply(target, dest);

		var pawn = parent.pawn;
		var building = target.Thing;
		if (building is not { Spawned: true })
		{
			return;
		}

		FleckMaker.ThrowDustPuff(building.TrueCenter(), building.Map, 1f);
		building.TakeDamage(new DamageInfo(DamageDefOf.Bite, Props.damage, instigator: pawn));

		var food = pawn.needs?.food;
		if (food != null)
		{
			food.CurLevelPercentage += Props.nutritionGain;
		}
	}

	public override bool Valid(
		LocalTargetInfo target,
		bool throwMessages = false
	)
	{
		if (!IsWooden(target.Thing))
		{
			if (throwMessages)
			{
				Messages.Message(
					"Ordo_SillyAnimalThings_GnawNotWooden".Translate(),
					target.ToTargetInfo(parent.pawn.Map),
					MessageTypeDefOf.RejectInput,
					historical: false
				);
			}

			return false;
		}

		return base.Valid(target, throwMessages);
	}
}
