using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using Ordo.DisallowedZone;
using RimWorld;
using Verse;
using Verse.AI;
using ZoneComp = Ordo.DisallowedZone.DisallowedZone;

namespace Ordo.BridgeTools.Scenarios;

// The zone is a map component, not a thing on the stage, so every scenario clears it in a finally.
public static class DisallowedZoneScenarios
{
    // Suites: "setup" for defs and patches, "area" for what pawns may use, "ai" for pawns acting on it, "path" for routes
    public static readonly List<Scenario> All = new()
    {
        new("setup_patches_and_designators", "setup", PatchesAndDesignators),
        new("area_unrestricted_pawns", "area", UnrestrictedPawns),
        new("area_restricted_pawns_follow_changes", "area", RestrictedPawnsFollowChanges),
        new("area_toggle_needs_cells", "area", ToggleNeedsCells),
        new("ai_pawns_leave_zone", "ai", PawnsLeaveZone),
        new("ai_job_in_zone_interrupted", "ai", JobInZoneInterrupted),
        new("ai_hauling_skips_zone", "ai", HaulingSkipsZone),
        new("path_routes_around_zone", "path", RoutesAroundZone)
    };

    private static ZoneComp ZoneOf(
        ScenarioContext context
    ) => ZoneComp.For(context.Map);

    // Paints a rectangle of the zone, relative to the site
    private static void Paint(
        ScenarioContext context,
        int x1,
        int z1,
        int x2,
        int z2
    )
    {
        var zone = ZoneOf(context);
        for (var x = x1; x <= x2; x++)
            for (var z = z1; z <= z2; z++)
                zone.Set(context.Cell(x, z), true);
    }

    private static void Reset(
        ScenarioContext context
    )
    {
        var zone = ZoneOf(context);
        zone.SetActive(false);
        foreach (var cell in context.Map.AllCells)
            zone.Set(cell, false);
    }

    private static Task PatchesAndDesignators(
        ScenarioContext context
    )
    {
        context.Check("the map has a DisallowedZone component", ZoneOf(context) != null);

        var getter = AccessTools.PropertyGetter(typeof(Pawn_PlayerSettings), nameof(Pawn_PlayerSettings.EffectiveAreaRestrictionInPawnCurrentMap));
        var owners = Harmony.GetPatchInfo(getter)?.Postfixes.Select(patch => patch.owner).ToList() ?? new List<string>();
        context.Check($"the effective area getter has the mod's postfix; owners: [{string.Join(", ", owners)}]", owners.Contains("Ordo.DisallowedZone"));

        var dataForArea = AccessTools.Method(typeof(AreaSource), nameof(AreaSource.DataForArea));
        context.Check("AreaSource.DataForArea has the mod's prefix",
            Harmony.GetPatchInfo(dataForArea)?.Prefixes.Any(patch => patch.owner == "Ordo.DisallowedZone") == true);

        var zoneTab = DefDatabase<DesignationCategoryDef>.GetNamed("Zone");
        var classes = zoneTab.specialDesignatorClasses ?? new List<System.Type>();
        context.Check("the Zone tab has the expand, clear and toggle designators",
            classes.Contains(typeof(Designator_DisallowedZoneExpand))
            && classes.Contains(typeof(Designator_DisallowedZoneClear))
            && classes.Contains(typeof(Designator_DisallowedZoneToggle)));
        context.Check("the toggle key binding loaded", KeyBindings.Ordo_ToggleDisallowedZone != null);
        context.Check("the toggle icon loaded", Textures.Toggle != null && Textures.Toggle != BaseContent.BadTex);
        return Task.CompletedTask;
    }

    private static Task UnrestrictedPawns(
        ScenarioContext context
    )
    {
        try
        {
            var colonist = context.Worker(-14, -14, WorkTypeDefOf.Hauling);
            var animal = context.Spawn("Husky", -13, -14, PawnMode.Tamed);
            Paint(context, -6, -16, -1, 16);
            var inside = context.Cell(-4, -11);
            var outside = context.Cell(4, -11);

            context.Check("while off, the zone doesn't restrict anyone", inside.InAllowedArea(colonist) && inside.InAllowedArea(animal));
            context.Check("while off, the colonist's effective area is still unrestricted",
                colonist.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap == null);

            ZoneOf(context).SetActive(true);
            context.Check("while on, an unrestricted colonist may not use the zone", !inside.InAllowedArea(colonist) && inside.IsForbidden(colonist));
            context.Check("while on, an unrestricted colonist may use cells outside it", outside.InAllowedArea(colonist));
            context.Check("while on, a tamed animal may not use the zone", inside.IsForbidden(animal));

            colonist.drafter.Drafted = true;
            context.Check("a drafted colonist ignores the zone", !inside.IsForbidden(colonist));
            colonist.drafter.Drafted = false;

            ZoneOf(context).SetActive(false);
            context.Check("switched off, the colonist may use the zone again", inside.InAllowedArea(colonist) && !inside.IsForbidden(colonist));
        }
        finally
        {
            Reset(context);
        }
        return Task.CompletedTask;
    }

    private static Task RestrictedPawnsFollowChanges(
        ScenarioContext context
    )
    {
        Area_Allowed area = null;
        try
        {
            context.Check("made an allowed area", context.Map.areaManager.TryMakeNewAllowed(out area));
            for (var x = -16; x <= 4; x++)
                for (var z = -16; z <= 16; z++)
                    area[context.Cell(x, z)] = true;
            var colonist = context.Worker(-14, -14, WorkTypeDefOf.Hauling);
            colonist.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            Paint(context, -6, -16, -1, 16);
            ZoneOf(context).SetActive(true);

            var effective = colonist.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap as EffectiveArea;
            context.Check("the colonist's effective area is the mod's", effective != null && effective.BaseArea == area);
            context.Check("the effective area keeps the area's label", effective?.Label == area.Label);
            context.Check("in the area and outside the zone is allowed", context.Cell(-11, -11).InAllowedArea(colonist));
            context.Check("in the area and in the zone is not", !context.Cell(-4, -11).InAllowedArea(colonist));
            context.Check("outside the area is still not allowed", !context.Cell(9, -11).InAllowedArea(colonist));

            area[context.Cell(9, -11)] = true;
            context.Check("painting the pawn's area while on takes effect", context.Cell(9, -11).InAllowedArea(colonist));
            ZoneOf(context).Set(context.Cell(-11, -11), true);
            context.Check("growing the zone while on takes effect", !context.Cell(-11, -11).InAllowedArea(colonist));
            ZoneOf(context).Set(context.Cell(-4, -11), false);
            context.Check("shrinking the zone while on takes effect", context.Cell(-4, -11).InAllowedArea(colonist));

            area.Invert();
            context.Check("inverting the pawn's area while on takes effect",
                context.Cell(12, -11).InAllowedArea(colonist) && !context.Cell(-14, -10).InAllowedArea(colonist));

            area.Delete();
            area = null;
            var afterDelete = colonist.playerSettings.EffectiveAreaRestrictionInPawnCurrentMap as EffectiveArea;
            context.Check("with the area deleted, the colonist is unrestricted minus the zone",
                afterDelete != null && afterDelete.BaseArea == null && !context.Cell(-3, -11).InAllowedArea(colonist) && context.Cell(12, -11).InAllowedArea(colonist));
        }
        finally
        {
            area?.Delete();
            Reset(context);
        }
        return Task.CompletedTask;
    }

    private static Task ToggleNeedsCells(
        ScenarioContext context
    )
    {
        try
        {
            var zone = ZoneOf(context);
            zone.SetActive(true);
            context.Check("an empty zone can't be switched on", !zone.Active);

            Paint(context, -6, -6, -6, -6);
            zone.SetActive(true);
            context.Check("a painted zone can be switched on", zone.Active && ZoneComp.ActiveZones.Contains(zone));

            zone.Set(context.Cell(-6, -6), false);
            context.Check("clearing the last cell switches it off", !zone.Active && !ZoneComp.ActiveZones.Contains(zone));
        }
        finally
        {
            Reset(context);
        }
        return Task.CompletedTask;
    }

    // Vanilla's seek-allowed-area job giver walks them out, as it would for a pawn outside its area
    private static async Task PawnsLeaveZone(
        ScenarioContext context
    )
    {
        try
        {
            var colonist = context.Worker(-4, 0, WorkTypeDefOf.Hauling);
            var animal = context.Spawn("Husky", -3, 0, PawnMode.Tamed);
            Paint(context, -8, -4, 1, 4);
            var zone = ZoneOf(context);
            zone.SetActive(true);

            await context.Until("the colonist and the animal walk out of the zone",
                () => !zone[colonist.Position] && !zone[animal.Position], 1500);
            if (zone[colonist.Position] || zone[animal.Position])
                context.Fail($"still inside: {Stage.Describe(colonist)}; {Stage.Describe(animal)}");
        }
        finally
        {
            Reset(context);
        }
    }

    // As vanilla ends a job when a pawn's area changes, switching the zone on ends jobs that target it
    private static async Task JobInZoneInterrupted(
        ScenarioContext context
    )
    {
        try
        {
            var colonist = context.Worker(-14, -14, WorkTypeDefOf.Hauling);
            Paint(context, 4, -16, 9, 16);
            var target = context.Cell(6, -14);
            var job = JobMaker.MakeJob(JobDefOf.Goto, target);
            colonist.jobs.StartJob(job, JobCondition.InterruptForced);
            await context.Step(2);
            context.Check("the colonist set off for the cell", colonist.CurJob == job);

            ZoneOf(context).SetActive(true);
            context.Check($"switching the zone on ended the job: {Stage.Describe(colonist)}", colonist.CurJob != job);
        }
        finally
        {
            Reset(context);
        }
    }

    private static async Task HaulingSkipsZone(
        ScenarioContext context
    )
    {
        Zone_Stockpile stockpile = null;
        try
        {
            stockpile = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, context.Map.zoneManager);
            context.Map.zoneManager.RegisterZone(stockpile);
            for (var x = -16; x <= -13; x++)
                for (var z = -16; z <= -13; z++)
                    stockpile.AddCell(context.Cell(x, z));

            var colonist = context.Worker(-11, -11, WorkTypeDefOf.Hauling);
            var inZone = context.Place(ThingDefOf.Steel, 4, 4, stackCount: 10);
            var outside = context.Place(ThingDefOf.Steel, -6, 4, stackCount: 10);
            Paint(context, 1, 1, 7, 7);
            ZoneOf(context).SetActive(true);

            await context.Until("the colonist hauls the steel outside the zone", () => stockpile.ContainsCell(outside.PositionHeld) && outside.Spawned, 3000);
            await context.Step(600);
            context.Check($"the steel in the zone wasn't touched: {Stage.Describe(colonist)}", inZone.Spawned && inZone.Position == context.Cell(4, 4));

            ZoneOf(context).SetActive(false);
            await context.Until("switched off, the steel in the zone gets hauled", () => !inZone.Spawned || stockpile.ContainsCell(inZone.PositionHeld), 3000);
        }
        finally
        {
            Reset(context);
            stockpile?.Delete(false);
        }
    }

    // Vanilla charges extra for path cells outside a pawn's area, so routes go around the zone when there's a way. The path
    // finder reuses its cost grids until it gathers a change to the map's path data, at most once a tick, so each check comes a
    // tick after the change; a zone edit the path finder isn't told about would leave the old grid in use.
    private static async Task RoutesAroundZone(
        ScenarioContext context
    )
    {
        try
        {
            var colonist = context.Worker(-11, -11, WorkTypeDefOf.Hauling);
            // A wall of zone across the whole site
            Paint(context, -1, -16, 1, 16);
            var zone = ZoneOf(context);
            var destination = context.Cell(12, -11);

            int ZoneCellsOnPath()
            {
                using var path = context.Map.pathFinder.FindPathNow(colonist.Position, destination, colonist);
                if (!path.Found)
                {
                    context.Fail("no path found");
                    return -1;
                }
                return path.NodesReversed.Count(cell => zone[cell]);
            }

            var off = ZoneCellsOnPath();
            context.Check($"switched off, the route goes straight through the zone ({off} zone cells)", off > 0);

            zone.SetActive(true);
            await context.Step(1);
            var closed = ZoneCellsOnPath();
            context.Check($"switched on with no way around, the route still crosses the zone ({closed} zone cells)", closed > 0);

            // Clear a gap at the top: the route now goes around through it
            for (var z = 11; z <= 16; z++)
                for (var x = -1; x <= 1; x++)
                    zone.Set(context.Cell(x, z), false);
            await context.Step(1);
            var gap = ZoneCellsOnPath();
            context.Check($"with a gap cleared, the route goes around the zone ({gap} zone cells)", gap == 0);
        }
        finally
        {
            Reset(context);
        }
    }
}
