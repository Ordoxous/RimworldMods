using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using JetBrains.Annotations;
using RimWorld;
using UnityEngine;
using Verse;

namespace Ordo.BlueprintMaterials;

[StaticConstructorOnStartup]
public class ReplaceBlueprintMaterials
{
    // Defs don't change at runtime, so these lookups are built once per key.
    private static readonly Dictionary<DesignatorDropdownGroupDef, List<(TerrainDef def, ThingDef materialDef)>> SimilarTerrainCache = new();
    private static readonly Dictionary<BuildableDef, List<ThingDef>> StuffCache = new();

    static ReplaceBlueprintMaterials()
    {
        var harmony = new Harmony(typeof(ReplaceBlueprintMaterials).Namespace);

        harmony.Patch(
            AccessTools.Method(typeof(Blueprint_Build), nameof(Blueprint_Build.GetGizmos)),
            postfix: new HarmonyMethod(Postfix_Blueprint_Build_GetGizmos)
        );
    }

    private static List<(TerrainDef def, ThingDef materialDef)> GetSimilarTerrain(DesignatorDropdownGroupDef groupDef)
    {
        if (SimilarTerrainCache.TryGetValue(groupDef, out var cached))
            return cached;

        cached = DefDatabase<TerrainDef>
            .AllDefsListForReading
            .Where(def => def.designatorDropdown == groupDef)
            .Select(def => (def, materialDef: def.CostList.FirstOrDefault()?.thingDef))
            .Where(entry => entry.materialDef is not null)
            .ToList();
        SimilarTerrainCache[groupDef] = cached;
        return cached;
    }

    private static List<ThingDef> GetPossibleStuffs(BuildableDef entityDef)
    {
        if (StuffCache.TryGetValue(entityDef, out var cached))
            return cached;

        cached = GenStuff.AllowedStuffsFor(entityDef).ToList();
        StuffCache[entityDef] = cached;
        return cached;
    }

    private static bool IsAvailable(ThingDef materialDef, Map map) =>
        DebugSettings.godMode || map.listerThings.ThingsOfDef(materialDef).Count > 0;

    // Plain loops: these run every frame for every selected blueprint.
    private static bool HasAlternative(List<(TerrainDef def, ThingDef materialDef)> similarDefs, TerrainDef current, Map map)
    {
        foreach (var (def, materialDef) in similarDefs)
            if (def != current && IsAvailable(materialDef, map))
                return true;
        return false;
    }

    private static bool HasAlternative(List<ThingDef> stuffs, ThingDef current, Map map)
    {
        foreach (var stuff in stuffs)
            if (stuff != current && IsAvailable(stuff, map))
                return true;
        return false;
    }

    static IEnumerable<Gizmo> Postfix_Blueprint_Build_GetGizmos(
        IEnumerable<Gizmo> values,
        Blueprint_Build __instance
    )
    {
        foreach (var value in values)
           yield return value;

        if (TryBuildAlternativeTerrainGizmo(__instance, out var gizmo))
        {
            yield return gizmo;
        }

        if (TryBuildAlternativeMaterialsGizmo(__instance, out var action))
        {
            yield return action;
        }
    }

    private static bool TryBuildAlternativeTerrainGizmo(
        Blueprint_Build blueprint,
        [CanBeNull] out Command_Action action
    )
    {
        var entityDef = blueprint.def.entityDefToBuild;
        if (entityDef is not TerrainDef { designatorDropdown: { } groupDef } terrainDef) {
            action = null;
            return false;
        }

        var map = blueprint.Map;
        var similarDefs = GetSimilarTerrain(groupDef);

        var command = new Command_Action_AlternativeMaterial(blueprint, HasAlternative(similarDefs, terrainDef, map))
        {
            icon = terrainDef.uiIcon,
            defaultIconColor = entityDef.uiIconColor,
            iconTexCoords = Widgets.CroppedTerrainTextureRect(terrainDef.uiIcon),
        };
        command.action = () =>
        {
            var floatMenuOptionList = similarDefs
                .Where(entry => entry.def == terrainDef || IsAvailable(entry.materialDef, map))
                .Select(entity =>
                    {
                        var (def, materialDef) = entity;

                        return new FloatMenuOption(
                            materialDef.LabelAsStuff,
                            () =>
                            {
                                foreach (var selected in command.SelectedBlueprints().ToList()) {
                                    selected.Destroy(DestroyMode.Cancel);
                                    var newBlueprint = GenConstruct.PlaceBlueprintForBuild(def, selected.Position, map, def.defaultPlacingRot, Faction.OfPlayer, null);
                                    Find.Selector.Select(newBlueprint, false, false);
                                }
                            },
                            materialDef.uiIcon,
                            materialDef.uiIconColor
                        );
                    }
                ).ToList();

            Find.WindowStack.Add(new FloatMenu(floatMenuOptionList));
        };

        action = command;
        return true;
    }

    private static bool TryBuildAlternativeMaterialsGizmo(Blueprint_Build blueprint, out Command_Action action)
    {
        if (blueprint.stuffToUse is not { } currentStuff)
        {
            action = null;
            return false;
        }

        var map = blueprint.Map;
        var entityDef = blueprint.BuildDef;
        var selectedStyle = blueprint.selectedStyleDef ?? blueprint.StyleDef;
        var possibleStuffs = GetPossibleStuffs(entityDef);

        var command = new Command_Action_AlternativeMaterial(blueprint, HasAlternative(possibleStuffs, currentStuff, map))
        {
            icon = Widgets.GetIconFor(entityDef, currentStuff, selectedStyle),
            defaultIconColor = entityDef.GetColorForStuff(currentStuff),
            iconProportions = entityDef.graphicData.drawSize.RotatedBy(entityDef.defaultPlacingRot),
            iconDrawScale = GenUI.IconDrawScale(entityDef),
        };
        command.action = () => {
            var floatMenuOptionList = possibleStuffs
                .Where(def => def == currentStuff || IsAvailable(def, map))
                .Select(stuff => new FloatMenuOption(
                        stuff.LabelAsStuff,
                        () => {
                            foreach (var selected in command.SelectedBlueprints())
                                selected.stuffToUse = stuff;
                        },
                        stuff.uiIcon,
                        stuff.uiIconColor
                    )
                ).ToList();

            Find.WindowStack.Add(new FloatMenu(floatMenuOptionList));
        };

        action = command;
        return true;
    }
}

public class Command_Action_AlternativeMaterial : Command_Action {

    private static readonly CachedTexture ChangeStyleTex = new("UI/Gizmos/ChangeStyle");

    private readonly ThingDef _blueprintDef;
    private readonly ThingDef _currentMaterial;

    // Floors carry their material in the blueprint def, so their stuffToUse is null.
    public Command_Action_AlternativeMaterial(Blueprint_Build blueprint, bool hasAlternative)
    {
        _blueprintDef = blueprint.def;
        _currentMaterial = blueprint.stuffToUse;
        defaultLabel = "Ordo_BlueprintMaterials_SelectMaterial".Translate();
        groupable = true;
        if (!hasAlternative)
            Disable("Ordo_BlueprintMaterials_OnlyOneMaterial".Translate());
        Order = 15f;
    }

    // Formatted only when the tooltip is shown, not every time the gizmo is built.
    public override string Desc => "Ordo_BlueprintMaterials_SelectMaterialDesc".Translate(_blueprintDef.entityDefToBuild.LabelCap);

    private bool Matches(Blueprint_Build blueprint) =>
        blueprint.def == _blueprintDef && blueprint.stuffToUse == _currentMaterial;

    // Same rule as grouping, so a click changes exactly the blueprints this gizmo stands for.
    public IEnumerable<Blueprint_Build> SelectedBlueprints() =>
        Find.Selector.SelectedObjects.OfType<Blueprint_Build>().Where(Matches);

    public override bool GroupsWith(Gizmo other)
    {
        return other is Command_Action_AlternativeMaterial otherMaterial
               && otherMaterial._blueprintDef == _blueprintDef
               && otherMaterial._currentMaterial == _currentMaterial
               && base.GroupsWith(other);
    }

    public override GizmoResult GizmoOnGUI(Vector2 topLeft, float maxWidth, GizmoRenderParms parms)
    {
        var baseGizmo = base.GizmoOnGUI(topLeft, maxWidth, parms);
        Designator_Dropdown.DrawExtraOptionsIcon(topLeft, GetWidth(maxWidth));

        Widgets.DrawTextureFitted(
            new Rect(topLeft.x, topLeft.y, GetWidth(maxWidth), 75f),
            ChangeStyleTex.Texture,
            iconDrawScale * 0.5f,
            iconProportions,
            iconTexCoords,
            iconAngle
        );

        return baseGizmo;
    }
}
