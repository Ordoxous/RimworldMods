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

    static ReplaceBlueprintMaterials()
    {
        var harmony = new Harmony(typeof(ReplaceBlueprintMaterials).Namespace);

        harmony.Patch(
            typeof(Blueprint_Build).GetMethod(nameof(Blueprint_Build.GetGizmos)),
            postfix: new HarmonyMethod(Postfix_Blueprint_Build_GetGizmos)
        );
    }

    private static IEnumerable<Blueprint_Build> GetSelectedBlueprintsOfType(ThingDef def) {
        return Find.Selector.SelectedObjects
            .Where(s => s is Blueprint_Build)
            .Cast<Blueprint_Build>()
            .Where(t => t.def == def);
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

        var similarDefs = DefDatabase<TerrainDef>
            .AllDefsListForReading
            .Where(def => def.designatorDropdown == groupDef)
            .Select(def => (def, def.CostList.FirstOrDefault()?.thingDef))
            .Where(entry => {
                    var (def, materialDef) = entry;
                    return materialDef is not null
                           && (DebugSettings.godMode || def == entityDef || Find.CurrentMap.listerThings.ThingsOfDef(materialDef).Count > 0);
                }
            )
            .ToList();

        var allowedMaterials = similarDefs.Select(s => s.thingDef).ToList();

        // TODO: Keep this from grouping with other's of the same group but different materials
        action = new Command_Action_AlternativeMaterial(terrainDef, entityDef.CostList.FirstOrDefault()?.thingDef, allowedMaterials)
        {
            icon = terrainDef.uiIcon,
            defaultIconColor = entityDef.uiIconColor,
            iconTexCoords = Widgets.CroppedTerrainTextureRect(terrainDef.uiIcon),
            action = () =>
            {
                var floatMenuOptionList = similarDefs
                    .Select(entity =>
                        {
                            var (def, materialDef) = entity;

                            return new FloatMenuOption(
                                materialDef.LabelAsStuff,
                                () =>
                                {
                                    var selectedBlueprints = GetSelectedBlueprintsOfType(blueprint.def).ToList();
                                    foreach (var selected in selectedBlueprints) {
                                        selected.Destroy(DestroyMode.Cancel);
                                        var newBlueprint = GenConstruct.PlaceBlueprintForBuild(def, selected.Position, Find.CurrentMap, def.defaultPlacingRot, Faction.OfPlayer, null);
                                        Find.Selector.Select(newBlueprint, false, false);
                                    }
                                },
                                materialDef.uiIcon,
                                materialDef.uiIconColor
                            );
                        }
                    ).ToList();

                Find.WindowStack.Add(new FloatMenu(floatMenuOptionList));
            }
        };

        return true;
    }

    private static bool TryBuildAlternativeMaterialsGizmo(Blueprint_Build blueprint, out Command_Action action)
    {
        if (blueprint.stuffToUse == null)
        {
            action = null;
            return false;
        }

        var entityDef = blueprint.BuildDef;
        var selectedStyle = blueprint.selectedStyleDef ?? blueprint.StyleDef;

        var allowedStuffs = Find.CurrentMap
                .resourceCounter
                .AllCountedAmounts
                .Keys
                .Where(def => def.IsStuff
                              && def.stuffProps.CanMake(entityDef)
                              && (DebugSettings.godMode || def == entityDef || Find.CurrentMap.listerThings.ThingsOfDef(def).Count > 0))
            .ToList();

        action = new Command_Action_AlternativeMaterial(entityDef, blueprint.stuffToUse, allowedStuffs)
        {
            icon = Widgets.GetIconFor(entityDef, blueprint.stuffToUse, selectedStyle),
            defaultIconColor = blueprint.stuffToUse != null ? entityDef.GetColorForStuff(blueprint.stuffToUse) : entityDef.uiIconColor,
            iconProportions = entityDef.graphicData.drawSize.RotatedBy(entityDef.defaultPlacingRot),
            iconDrawScale = GenUI.IconDrawScale(entityDef),
            action = () => {
                var floatMenuOptionList = allowedStuffs
                    .Select(stuff => new FloatMenuOption(
                            stuff.LabelAsStuff,
                            () => {
                                foreach (var selected in GetSelectedBlueprintsOfType(blueprint.def))
                                    selected.stuffToUse = stuff;
                            },
                            stuff.uiIcon,
                            stuff.uiIconColor
                        )
                    ).ToList();

                Find.WindowStack.Add(new FloatMenu(floatMenuOptionList));
            }
        };

        return true;
    }
}

public class Command_Action_AlternativeMaterial : Command_Action {

    private static readonly CachedTexture ChangeStyleTex = new("UI/Gizmos/ChangeStyle");

    private readonly ThingDef _currentMaterial;

    public Command_Action_AlternativeMaterial(
        BuildableDef entityDef,
        ThingDef currentMaterial,
        IList<ThingDef> allowedDefs
    )
    {
        _currentMaterial = currentMaterial;
        defaultLabel = "Select Material";
        defaultDesc = $"Update the selected material for this ({entityDef.LabelCap}) blueprint";
        groupable = true;
        disabled = allowedDefs.Count == 1;
        disabledReason = "Only one material available";
        Order = 15f;
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