using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using PeteTimesSix.SimpleSidearms;
using PeteTimesSix.SimpleSidearms.Compat;
using PeteTimesSix.SimpleSidearms.Intercepts;
using PeteTimesSix.SimpleSidearms.Utilities;
using RimWorld;
using SimpleSidearms.rimworld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Ordo.Tweaks;

// Tools for work, with Simple Sidearms carrying them. Simple Sidearms already gives every toil a pre-init action that takes out
// the carried weapon boosting the toil's stats (ToilMaker_MakeToil_Patches.CheckForTools). A prefix replaces that method with
// a copy that keeps its bookkeeping (autotoolToil, autotoolJob, the idle delay) but picks the stats from ToolWorkDef, scores
// tools by the sum of their offsets with penalties ruling them out, and puts a tool that slows walking away for walks.
//
// Nothing here may be reached without Simple Sidearms: its types are only touched from NoInlining methods, called when it's
// active, and no field, signature or cached delegate names one of its types.
[StaticConstructorOnStartup]
public static class ToolsForWork
{
    public const string SimpleSidearmsId = "PeteTimesSix.SimpleSidearms";

    public static readonly bool SimpleSidearmsActive = ModsConfig.IsActive(SimpleSidearmsId);

    static ToolsForWork()
    {
        if (!SimpleSidearmsActive)
        {
            return;
        }

        Patch(new Harmony(typeof(ToolsForWork).Namespace));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Patch(Harmony harmony)
    {
        harmony.TryPatch(
            AccessTools.Method(typeof(ToilMaker_MakeToil_Patches), nameof(ToilMaker_MakeToil_Patches.CheckForTools)),
            "Simple Sidearms' ToilMaker_MakeToil_Patches.CheckForTools",
            "Simple Sidearms picks work tools itself (largest bonus, penalties ignored, tools kept on walks)",
            prefix: new HarmonyMethod(typeof(ToolsForWork), nameof(Prefix_CheckForTools))
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(CompSidearmMemory), nameof(CompSidearmMemory.InformOfAddedSidearm)),
            "Simple Sidearms' CompSidearmMemory.InformOfAddedSidearm",
            "fetched tools aren't marked forced in Auto Arm",
            postfix: new HarmonyMethod(typeof(ToolsFetch), nameof(ToolsFetch.Postfix_InformOfAddedSidearm))
        );
        harmony.TryPatch(
            AccessTools.Method(typeof(GettersFilters), nameof(GettersFilters.findBestMeleeWeapon)),
            "Simple Sidearms' GettersFilters.findBestMeleeWeapon",
            "a pawn with no preferred melee weapon can take out a tool that slows walking, such as the jackhammer, as their everyday weapon",
            postfix: new HarmonyMethod(typeof(ToolsForWork), nameof(Postfix_findBestMeleeWeapon))
        );
    }

    // Simple Sidearms' preference, for a pawn with no preferred melee weapon, takes the carried melee weapon with the best
    // damage, which can be the jackhammer: the pawn then idles and walks with it, and the think tree runs this mid-job too. Out
    // of a fight, a tool that slows walking is passed over for the next best. Drafted, attacked or in a mental state, it isn't.
    public static void Postfix_findBestMeleeWeapon(Pawn pawn, ref ThingWithComps result, ref bool __result)
    {
        if (!__result || result == null || !ToolsCategory.IsTool(result.def) || !ToolStats.SlowsWalking(result.def) || !TweaksSettings.ToolsSwitchForWork)
        {
            return;
        }

        if (pawn.Drafted || pawn.InMentalState || pawn.mindState?.meleeThreat != null || pawn.mindState?.enemyTarget != null)
        {
            return;
        }

        result = Switching.BestMeleeExceptSlowTools(pawn);
        __result = result != null;
    }

    // Called for every toil made, from Simple Sidearms' postfixes on ToilMaker.MakeToil and the Toil constructor. The setting
    // is read here, so turning it off applies to toils made afterwards.
    public static bool Prefix_CheckForTools(Toil toil)
    {
        if (toil == null || !TweaksSettings.ToolsSwitchForWork)
        {
            return true;
        }

        return Switching.AddActions(toil);
    }

    // Simple Sidearms' own "Tool use autoswitch": without it, fetched tools would never come out
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static bool SidearmsToolAutoSwitch() => PeteTimesSix.SimpleSidearms.SimpleSidearms.Settings.ToolAutoSwitch;

    public static class Switching
    {
        // Reused by PreInit, which only runs on the main thread
        private static readonly HashSet<StatDef> Stats = new();

        private const float Epsilon = 0.0001f;

        // Returns whether Simple Sidearms' own method should still run: only when its tool switching is off, in which case it
        // does nothing either
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool AddActions(Toil toil)
        {
            if (!PeteTimesSix.SimpleSidearms.SimpleSidearms.Settings.ToolAutoSwitch)
            {
                return true;
            }

            toil.AddPreInitAction(() => PreInit(toil));
            toil.AddFinishAction(() => Finish(toil));
            return false;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void PreInit(Toil toil)
        {
            var pawn = toil.GetActor();
            if (pawn == null || !pawn.IsValidSidearmsCarrierRightNow() || pawn.Drafted)
            {
                return;
            }

            var memory = CompSidearmMemory.GetMemoryCompForPawn(pawn);
            if (memory == null)
            {
                return;
            }

            memory.CheckIfStillOnAutotoolJob();
            switch (toil.defaultCompleteMode)
            {
                // Reservations, pickups and the like: too short to be worth a swap
                case ToilCompleteMode.Instant:
                    return;
                case ToilCompleteMode.PatherArrival:
                    PutAwaySlowTool(pawn, memory);
                    return;
            }

            Stats.Clear();
            ToolStats.ForJob(pawn.CurJob, Stats);
            if (Stats.Count == 0)
            {
                // Jobs no ToolWorkDef row names: Simple Sidearms' own choice, by the toil's skill or the recipe's
                var skill = toil.activeSkill?.Invoke() ?? pawn.CurJob?.RecipeDef?.workSkill;
                if (skill != null && SkillStatMap.Map.TryGetValue(skill, out var skillStats))
                {
                    Stats.UnionWith(skillStats);
                }
            }

            if (Stats.Count == 0)
            {
                return;
            }

            var best = BestCarried(pawn, Stats);
            if (best == null)
            {
                return;
            }

            if (best == pawn.equipment.Primary || WeaponAssingment.equipSpecificWeaponFromInventory(pawn, best, dropCurrent: false, intentionalDrop: false))
            {
                memory.autotoolToil = toil;
                memory.autotoolJob = pawn.CurJobDef;
            }
        }

        // As Simple Sidearms' own: a tool toil that ends starts the idle delay, which keeps the tool between back-to-back jobs
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Finish(Toil toil)
        {
            var pawn = toil.GetActor();
            if (pawn == null || !pawn.IsValidSidearmsCarrierRightNow())
            {
                return;
            }

            var memory = CompSidearmMemory.GetMemoryCompForPawn(pawn);
            if (memory == null)
            {
                return;
            }

            if (memory.autotoolToil == toil)
            {
                memory.delayIdleSwitchTimestamp = Find.TickManager.TicksGame;
            }
            else
            {
                memory.autotoolToil = null;
            }
        }

        // The best-scoring carried weapon for these stats; ties go to the lighter one, then the one in hand
        public static ThingWithComps BestCarried(Pawn pawn, ICollection<StatDef> stats)
        {
            ThingWithComps best = null;
            var bestScore = 0f;
            var bestMass = 0f;
            foreach (var weapon in pawn.GetCarriedWeapons(includeEquipped: true, includeTools: true))
            {
                var score = ToolStats.Score(weapon.def, stats);
                if (score <= 0f || !UsableBeside(pawn, weapon))
                {
                    continue;
                }

                var mass = weapon.GetStatValue(StatDefOf.Mass);
                var better = best == null
                             || score > bestScore + Epsilon
                             || (Mathf.Abs(score - bestScore) <= Epsilon
                                 && (mass < bestMass - Epsilon || (Mathf.Abs(mass - bestMass) <= Epsilon && weapon == pawn.equipment.Primary)));
                if (better)
                {
                    best = weapon;
                    bestScore = score;
                    bestMass = mass;
                }
            }

            return best;
        }

        // As Simple Sidearms' own choice: a weapon usable with the pawn's VFE shield, and one that can be an off-hand weapon
        // while Tacticowl's dual wielding has one in the other hand
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool UsableBeside(Pawn pawn, ThingWithComps weapon)
        {
            if (VFECore.active && VFECore.offHandShield(pawn) != null && !VFECore.usableWithShields(weapon.def))
            {
                return false;
            }

            return !Tacticowl.active || !Tacticowl.dualWieldActive() || !Tacticowl.getOffHand(pawn, out _) || Tacticowl.canBeOffHand(weapon.def);
        }

        // As Simple Sidearms' findBestMeleeWeapon (melee weapons only, as its preference asks), without tools that slow walking
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static ThingWithComps BestMeleeExceptSlowTools(Pawn pawn)
        {
            var candidates = pawn.GetCarriedWeapons(includeEquipped: true)
                .Where(weapon => weapon.def.IsMeleeWeapon && !(ToolsCategory.IsTool(weapon.def) && ToolStats.SlowsWalking(weapon.def)))
                .Where(weapon => PeteTimesSix.SimpleSidearms.SimpleSidearms.Settings.AllowBlockedWeaponUse || StatCalculator.canUseSidearmInstance(weapon, pawn, out _))
                .Where(weapon => UsableBeside(pawn, weapon))
                .Cast<Thing>()
                .ToList();
            if (candidates.Count == 0)
            {
                return null;
            }

            var bias = PeteTimesSix.SimpleSidearms.SimpleSidearms.Settings.SpeedSelectionBiasMelee;
            var averageSpeed = GettersFilters.AverageSpeedMelee(candidates, pawn);
            return (ThingWithComps)candidates.MaxBy(weapon => StatCalculator.getMeleeDPSBiased((ThingWithComps)weapon, pawn, bias, averageSpeed));
        }

        // Walks shorter than this keep the tool in hand: from one rock to the next, swapping out and back costs more than it saves
        public const float MinWalkToPutAway = 6f;

        // A tool that slows walking goes back for the walk, unless the player made it the pawn's weapon or the walk is short.
        // Other tools stay in hand, so a plant cutter doesn't swap between every plant.
        private static void PutAwaySlowTool(Pawn pawn, CompSidearmMemory memory)
        {
            var held = pawn.equipment.Primary;
            if (held == null || !ToolsCategory.IsTool(held.def) || !ToolStats.SlowsWalking(held.def) || memory.IsCurrentWeaponForced(alsoCountPreferredOrDefault: true))
            {
                return;
            }

            // The job's main target is where the walk goes for work jobs; without one, the walk counts as long
            var target = pawn.CurJob?.targetA ?? LocalTargetInfo.Invalid;
            if (target.IsValid && target.Cell.DistanceTo(pawn.Position) < MinWalkToPutAway)
            {
                return;
            }

            WeaponAssingment.equipBestWeaponFromInventoryByPreference(pawn, Enums.DroppingModeEnum.Calm);

            // With no preferred weapon, the best melee weapon can be the tool itself
            if (pawn.equipment.Primary == held)
            {
                WeaponAssingment.equipSpecificWeapon(pawn, null, dropCurrent: false, intentionalDrop: false);
            }
        }
    }
}

// Auto Arm's forced sidearms, by reflection: its ForcedWeapons class is internal. Auto Arm never swaps a forced sidearm for a
// different weapon, which keeps it off fetched tools.
internal static class AutoArmForced
{
    public static readonly bool Active = ModsConfig.IsActive("Snues.AutoArm");

    private static readonly Type ForcedWeapons = Active ? AccessTools.TypeByName("AutoArm.ForcedWeapons") : null;
    private static readonly MethodInfo AddSidearm = ForcedWeapons == null ? null : AccessTools.Method(ForcedWeapons, "AddSidearm", new[] { typeof(Pawn), typeof(ThingWithComps) });
    private static readonly MethodInfo RemoveForcedWeapon = ForcedWeapons == null ? null : AccessTools.Method(ForcedWeapons, "RemoveForcedWeapon", new[] { typeof(Pawn), typeof(ThingWithComps) });
    private static bool _warned;

    public static void Add(Pawn pawn, ThingWithComps tool) => Invoke(AddSidearm, pawn, tool);

    public static void Remove(Pawn pawn, ThingWithComps tool) => Invoke(RemoveForcedWeapon, pawn, tool);

    private static void Invoke(MethodInfo method, Pawn pawn, ThingWithComps tool)
    {
        if (!Active)
        {
            return;
        }

        if (method == null)
        {
            if (!_warned)
            {
                _warned = true;
                Log.Warning("[Tweaks] Couldn't find Auto Arm's ForcedWeapons.AddSidearm or RemoveForcedWeapon, so Auto Arm may swap fetched tools for weapons.");
            }

            return;
        }

        method.Invoke(null, new object[] { pawn, tool });
    }

    // As Auto Arm's own outfit check (Scoring.OutfitDisallows). Only with Auto Arm: it adds weapons to apparel policies, and
    // without it they don't list weapons at all.
    public static bool OutfitAllows(Pawn pawn, ThingWithComps tool)
    {
        var filter = pawn.outfits?.CurrentApparelPolicy?.filter;
        if (!Active || filter == null)
        {
            return true;
        }

        // Auto Arm adds weapons to the policies only when a new game starts. A policy that allows no weapon at all (a save
        // from before Auto Arm) hasn't been set up for them, so it doesn't stop tools.
        if (!filter.AllowedThingDefs.Any(def => def.IsWeapon))
        {
            return true;
        }

        if (!filter.Allows(tool.def) && !filter.OnlySpecialFilters)
        {
            return false;
        }

        return filter.AllowedQualityLevels == QualityRange.All
               || !tool.def.FollowQualityThingFilter()
               || !tool.TryGetQuality(out var quality)
               || filter.AllowedQualityLevels.Includes(quality);
    }
}
