using RimWorld;
using Verse;

namespace Ordo.NatureTab;

// Marks or unmarks a whole row for harvest, with the same checks as dragging vanilla's harvest designators over it
public static class FloraDesignations
{
    private static Designator_PlantsHarvest harvestDesignator;
    private static Designator_PlantsHarvestWood harvestWoodDesignator;

    private static Designator DesignatorFor(FloraRow row) =>
        row.IsTree
            ? harvestWoodDesignator ??= new Designator_PlantsHarvestWood()
            : harvestDesignator ??= new Designator_PlantsHarvest();

    // Whether the game would ever let the kind be harvested: not an anima tree, whose CompPlantPreventCutting forbids
    // cutting, nor a plant whose harvest tag isn't the standard one the harvest designator takes
    public static bool CanEverHarvest(FloraRow row)
    {
        if (!row.IsTree)
            return row.def.plant.harvestTag == "Standard";
        foreach (Plant plant in row.plants)
        {
            if (!plant.TryGetComp(out CompPlantPreventCutting comp) || !comp.PreventCutting)
                return true;
        }
        return false;
    }

    // Whether Harvest would mark anything now: the designator takes at least one of the row's plants
    public static bool CanHarvestNow(
        FloraRow row,
        Map map)
    {
        Designator designator = DesignatorFor(row);
        foreach (Plant plant in row.plants)
        {
            if (plant.Spawned && plant.Map == map && designator.CanDesignateThing(plant).Accepted)
                return true;
        }
        return false;
    }

    // Returns how many plants it marked. Designators work on the current map, so the row must come from it.
    public static int HarvestReady(
        FloraRow row,
        Map map)
    {
        Designator designator = DesignatorFor(row);
        DesignationManager designations = map.designationManager;
        int count = 0;
        foreach (Plant plant in row.plants)
        {
            if (!plant.Spawned || plant.Map != map || !designator.CanDesignateThing(plant).Accepted)
                continue;
            // Designator_Plants.DesignateThing, without the wood designator's message for every tree
            designations.RemoveAllDesignationsOn(plant);
            designations.AddDesignation(new Designation(plant, DesignationDefOf.HarvestPlant));
            count++;
        }

        if (count > 0 && row.IsTree && ModsConfig.IdeologyActive && row.def.plant.treeLoversCareIfChopped)
            Designator_PlantsHarvestWood.PossiblyWarnPlayerOnDesignatingTreeCut();
        return count;
    }

    // Returns how many designations it removed
    public static int Cancel(
        FloraRow row,
        Map map)
    {
        DesignationManager designations = map.designationManager;
        int count = 0;
        foreach (Plant plant in row.plants)
        {
            Designation designation = designations.DesignationOn(plant, DesignationDefOf.HarvestPlant);
            if (designation == null)
                continue;
            designations.RemoveDesignation(designation);
            count++;
        }
        return count;
    }
}
