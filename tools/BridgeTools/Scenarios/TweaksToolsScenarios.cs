using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Ordo.Tweaks;

namespace Ordo.BridgeTools.Scenarios;

// Tools for work (ToolsSwitchForWork, ToolsFetch), carried by Simple Sidearms. Simple Sidearms' types are reached by reflection,
// so BridgeTools loads without it. The tools come from Dark Ages: Medieval Tools (pickaxe, scythe, broom, bonesaw) and Setosa's
// Power Tools (the jackhammer, the one tool that slows walking).
public static partial class TweaksScenarios
{
    private const string SidearmsId = "PeteTimesSix.SimpleSidearms";
    private const string DarkAgesTools = SidearmsId + ",van.datools";
    private const string PowerTools = SidearmsId + ",setosa.power.tools";

    private static ThingWithComps MakeWeapon(string defName)
    {
        var def = ThingDef.Named(defName);
        return (ThingWithComps)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
    }

    private static ThingComp SidearmMemory(Pawn pawn) => pawn.AllComps.FirstOrDefault(comp => comp.GetType().Name == "CompSidearmMemory");

    // The test save's apparel policies predate Auto Arm, so they allow no weapon, and Auto Arm's policy enforcement drops every
    // weapon a pawn carries. Allowed here as Auto Arm sets up a new game's policies (idempotent; the save isn't kept).
    private static void PoliciesAllowWeapons()
    {
        if (!ModsConfig.IsActive("Snues.AutoArm"))
        {
            return;
        }

        foreach (var policy in Current.Game.outfitDatabase.AllOutfits)
        {
            policy.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
        }
    }

    // A knife in hand, remembered by Simple Sidearms as the pawn's preferred melee weapon
    private static ThingWithComps GiveKnife(Pawn pawn)
    {
        PoliciesAllowWeapons();
        var knife = MakeWeapon("MeleeWeapon_Knife");
        pawn.equipment.AddEquipment(knife);
        var memory = SidearmMemory(pawn);
        AccessTools.Method(memory.GetType(), "InformOfAddedPrimary").Invoke(memory, new object[] { knife });
        return knife;
    }

    // A tool in the inventory, as the player would give one: remembered by Simple Sidearms, not fetched by Tweaks
    private static ThingWithComps GiveTool(Pawn pawn, string defName)
    {
        PoliciesAllowWeapons();
        var tool = MakeWeapon(defName);
        pawn.inventory.innerContainer.TryAdd(tool);
        var memory = SidearmMemory(pawn);
        AccessTools.Method(memory.GetType(), "InformOfAddedSidearm").Invoke(memory, new object[] { tool });
        return tool;
    }

    private static bool Carries(Pawn pawn, Thing tool) => pawn.equipment.Primary == tool || pawn.inventory.innerContainer.Contains(tool);

    private static string Held(Pawn pawn) => pawn.equipment.Primary?.def.defName ?? "nothing";

    private static string ToilMode(Pawn pawn) => pawn.jobs.curDriver?.CurToil?.defaultCompleteMode.ToString() ?? "none";

    private static bool IsWalking(Pawn pawn) => pawn.jobs.curDriver?.CurToil?.defaultCompleteMode == ToilCompleteMode.PatherArrival;

    private static Thing Rock(ScenarioContext context, int x, int z)
    {
        var rock = context.Place(ThingDef.Named("Granite"), x, z);
        context.Map.designationManager.AddDesignation(new Designation(rock.Position, DesignationDefOf.Mine));
        return rock;
    }

    private static void HomeArea(ScenarioContext context)
    {
        foreach (var cell in CellRect.CenteredOn(context.Cell(0, 0), 12).ClipInsideMap(context.Map))
        {
            context.Map.areaManager.Home[cell] = true;
        }
    }

    private static Zone_Stockpile Stockpile(ScenarioContext context, int x, int z)
    {
        var zone = new Zone_Stockpile(StorageSettingsPreset.DefaultStockpile, context.Map.zoneManager);
        context.Map.zoneManager.RegisterZone(zone);
        foreach (var cell in CellRect.CenteredOn(context.Cell(x, z), 1))
        {
            zone.AddCell(cell);
        }

        return zone;
    }

    private static readonly List<Thing> PlacedTools = new();

    // A tool on the ground, in its default material
    private static ThingWithComps PlaceTool(ScenarioContext context, string defName, int x, int z)
    {
        PoliciesAllowWeapons();
        var def = ThingDef.Named(defName);
        var tool = (ThingWithComps)context.Place(def, x, z, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
        PlacedTools.Add(tool);
        return tool;
    }

    // Manual work priorities on, as most colonies have them; restored afterwards, with the fetched marks of the tools placed
    private static async Task WithWorkPriorities(Func<Task> run)
    {
        var was = Find.PlaySettings.useWorkPriorities;
        Find.PlaySettings.useWorkPriorities = true;
        PlacedTools.Clear();
        try
        {
            await run();
        }
        finally
        {
            Find.PlaySettings.useWorkPriorities = was;
            foreach (var tool in PlacedTools)
            {
                GameComponent_FetchedTools.Forget(tool);
            }

            PlacedTools.Clear();
        }
    }

    private static MapComponent_ToolAssignments Assignments(ScenarioContext context) => context.Map.GetComponent<MapComponent_ToolAssignments>();

    // 1, and Simple Sidearms alone for comparison. A miner with a knife and a jackhammer mines two rocks in a row: with the
    // setting on, the jackhammer is out for every pick hit and away for the walk to a rock 10 cells on. Simple Sidearms alone
    // takes it out at the first hit and keeps it to the second rock. With the rocks side by side, the walk is too short to
    // put it away: it's taken out once.
    private static Task ToolsWalkWithoutJackhammer(ScenarioContext context, bool on, bool adjacent = false) =>
        WithEquipRecorder(() => WalkWithoutJackhammer(context, on, adjacent));

    private static async Task WalkWithoutJackhammer(ScenarioContext context, bool on, bool adjacent)
    {
        await WithSetting("ToolsSwitchForWork", on ? "true" : "false", async () =>
        {
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            miner.skills.GetSkill(SkillDefOf.Mining).Level = 10;
            GiveKnife(miner);
            var jackhammer = GiveTool(miner, "AdvancedTool_Jackhammer");
            var first = Rock(context, 4, 0);
            var second = adjacent ? Rock(context, 4, 1) : Rock(context, 4, 10);
            var mining = 0;
            var miningWith = 0;
            var walkingWith = 0;
            var walkNotes = new List<string>();
            await context.Until($"both rocks are mined: miner {Stage.Describe(miner)}, holding {Held(miner)}", () =>
            {
                if (miner.CurJobDef == JobDefOf.Mine)
                {
                    if (IsWalking(miner))
                    {
                        if (miner.equipment.Primary == jackhammer)
                        {
                            walkingWith++;
                            walkNotes.Add($"{Find.TickManager.TicksGame} at {miner.Position} to {miner.CurJob.targetA.Cell} (toil {miner.jobs.curDriver.CurToilIndex})");
                        }
                    }
                    else if (ToilMode(miner) == nameof(ToilCompleteMode.Never))
                    {
                        mining++;
                        miningWith += miner.equipment.Primary == jackhammer ? 1 : 0;
                    }
                }

                return first.Destroyed && second.Destroyed;
            }, 6000, 1);
            context.Check($"the miner mined: {mining} ticks", mining > 0);
            var mineStats = new HashSet<StatDef>();
            ToolStats.ForJob(JobMaker.MakeJob(JobDefOf.Mine), mineStats);
            var why = $"stats [{string.Join(", ", mineStats.Select(stat => stat.defName))}], jackhammer score {ToolStats.Score(jackhammer.def, mineStats)}, carried [{string.Join(", ", miner.inventory.innerContainer.Select(thing => thing.def.defName))}], held {Held(miner)}, best {ToolsForWork.Switching.BestCarried(miner, mineStats)?.def.defName ?? "none"}, usable {ToolsForWork.Switching.UsableBeside(miner, jackhammer)}, offsets [{string.Join(", ", jackhammer.def.equippedStatOffsets?.Select(offset => $"{offset.stat.defName}={offset.value}") ?? Enumerable.Empty<string>())}]";
            context.Check($"the jackhammer was out for the digging: {miningWith} of {mining} ticks; {why}", miningWith > mining * 0.9f);
            var takenOut = EquippedBy.Count(entry => entry.StartsWith("AdvancedTool_Jackhammer"));
            if (adjacent)
            {
                context.Check($"between side-by-side rocks it stayed in hand: taken out {takenOut} times [{string.Join(" || ", EquippedBy)}]", takenOut == 1);
            }
            else if (on)
            {
                context.Check($"the jackhammer was away for every walk: held on {walkingWith} walking ticks [{string.Join(", ", walkNotes.Take(3))}]; equips [{string.Join(" || ", EquippedBy)}]; memory: {MemoryState(miner)}", walkingWith == 0);
                context.Check($"and taken out again for the second rock: {takenOut} times", takenOut >= 2);
            }
            else
            {
                context.Check($"Simple Sidearms alone keeps it for the walk to the second rock: held on {walkingWith} walking ticks", walkingWith > 0);
            }
        });
    }

    // 2. A plant cutter keeps the scythe for the walks between plants within one job
    private static async Task ToolsPlantCutterKeepsScythe(ScenarioContext context)
    {
        var cutter = context.Worker(0, 0, WorkTypeDefOf.PlantCutting);
        var knife = GiveKnife(cutter);
        var scythe = GiveTool(cutter, "DA_MeleeWeapon_Scythe");
        var plants = new List<Thing>();
        for (var x = 3; x <= 9; x += 3)
        {
            var plant = (Plant)context.Place(ThingDef.Named("Plant_Grass"), x, 0);
            plant.Growth = 1f;
            context.Map.designationManager.AddDesignation(new Designation(plant, DesignationDefOf.CutPlant));
            plants.Add(plant);
        }

        var cutting = 0;
        var cuttingWith = 0;
        var knifeAfterScythe = 0;
        var hadScythe = false;
        await context.Until($"the plants are cut: cutter {Stage.Describe(cutter)}, holding {Held(cutter)}", () =>
        {
            if (cutter.CurJobDef?.defName.StartsWith("CutPlant") == true)
            {
                hadScythe |= cutter.equipment.Primary == scythe;
                knifeAfterScythe += hadScythe && cutter.equipment.Primary == knife ? 1 : 0;
                if (!IsWalking(cutter) && cutter.jobs.curDriver?.CurToil?.defaultCompleteMode != ToilCompleteMode.Instant)
                {
                    cutting++;
                    cuttingWith += cutter.equipment.Primary == scythe ? 1 : 0;
                }
            }

            return plants.All(plant => plant.Destroyed);
        }, 3000, 1);
        context.Check($"the scythe was out for the cutting: {cuttingWith} of {cutting} ticks", cutting > 0 && cuttingWith > cutting * 0.9f);
        context.Check($"once out, it stayed out on the walks: the knife was back for {knifeAfterScythe} ticks", knifeAfterScythe == 0);
    }

    // 3. The bonesaw (operation speed +20%, surgery success -10%) is never used or fetched for surgery
    private static async Task ToolsBonesawNotForSurgery(ScenarioContext context)
    {
        var bonesaw = ThingDef.Named("DA_MeleeWeapon_Hacksaw");
        var surgery = DefDatabase<RecipeDef>.AllDefsListForReading.First(recipe => recipe.IsSurgery && recipe.workSpeedStat != null);
        var job = JobMaker.MakeJob(JobDefOf.DoBill);
        job.bill = new Bill_Medical(surgery, null);
        var stats = new HashSet<StatDef>();
        ToolStats.ForJob(job, stats);
        context.Check($"a surgery bill uses success chance: [{string.Join(", ", stats.Select(stat => stat.defName))}]", stats.Contains(StatDefOf.MedicalSurgerySuccessChance));
        context.Check($"the bonesaw is ruled out for it: score {ToolStats.Score(bonesaw, stats)}", ToolStats.Score(bonesaw, stats) < 0f);
        var doctorStats = ToolStats.ForWorkType(WorkTypeDefOf.Doctor);
        context.Check($"and for the Doctor work type: score {ToolStats.Score(bonesaw, doctorStats)}", ToolStats.Score(bonesaw, doctorStats) < 0f);

        var doctor = context.Worker(0, 0, WorkTypeDefOf.Doctor);
        GiveKnife(doctor);
        var carried = GiveTool(doctor, "DA_MeleeWeapon_Hacksaw");
        context.Check("a doctor carrying only the bonesaw has no tool for the surgery", ToolsForWork.Switching.BestCarried(doctor, stats) == null);
        doctor.inventory.innerContainer.Remove(carried);
        carried.Destroy();

        Stockpile(context, 6, 0);
        PlaceTool(context, "DA_MeleeWeapon_Hacksaw", 6, 0);
        await WithWorkPriorities(async () =>
        {
            Assignments(context).Refresh();
            context.Check($"a doctor isn't sent for the bonesaw: [{string.Join(", ", Assignments(context).ToFetch(doctor).Select(tool => tool.def.defName))}]", Assignments(context).ToFetch(doctor).Count == 0);
            await Task.CompletedTask;
        });
    }

    // Who equipped what, for a failure message: the frames from Simple Sidearms, Auto Arm and Tweaks
    private static readonly List<string> EquippedBy = new();

    public static void RecordRemoved(Thing item) => RecordEquip(item as ThingWithComps, "removed ");

    public static void RecordUnequipped(ThingWithComps eq) => RecordEquip(eq, "unequipped ");

    public static void RecordEquip(ThingWithComps newEq) => RecordEquip(newEq, "");

    private static void RecordEquip(ThingWithComps newEq, string what)
    {
        if (newEq == null)
        {
            return;
        }

        var frames = new System.Diagnostics.StackTrace().GetFrames()
            .Select(frame => frame.GetMethod())
            .Where(method => method?.DeclaringType != null && !method.DeclaringType.FullName!.StartsWith("System") && !method.DeclaringType.FullName.Contains("BridgeTools") && !method.DeclaringType.FullName.Contains("Harmony") && !method.DeclaringType.Name.StartsWith("ThingOwner") && !method.DeclaringType.Name.StartsWith("Gen") && !method.Name.StartsWith("Notify"))
            .Select(method => $"{method.DeclaringType.Name}.{method.Name}")
            .Take(6);
        EquippedBy.Add($"{what}{newEq.def.defName} at {Find.TickManager.TicksGame}: {string.Join(" < ", frames)}");
    }

    // 4, and the setting off. A cleaner carrying a broom switches to it for cleaning, which has no skill, so Simple Sidearms
    // alone never switches.
    private static Task ToolsCleanerUsesBroom(ScenarioContext context, bool on) => WithEquipRecorder(() => CleanerUsesBroom(context, on));

    // Records every weapon put in a pawn's hand while it runs, with who did it, into EquippedBy
    private static async Task WithEquipRecorder(Func<Task> run)
    {
        EquippedBy.Clear();
        var harmony = new Harmony(HarmonyId + ".equip");
        harmony.Patch(AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.AddEquipment)), postfix: new HarmonyMethod(typeof(TweaksScenarios), nameof(RecordEquip), new[] { typeof(ThingWithComps) }));
        harmony.Patch(AccessTools.Method(typeof(Pawn_InventoryTracker), nameof(Pawn_InventoryTracker.Notify_ItemRemoved)), postfix: new HarmonyMethod(typeof(TweaksScenarios), nameof(RecordRemoved)));
        harmony.Patch(AccessTools.Method(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentRemoved)), postfix: new HarmonyMethod(typeof(TweaksScenarios), nameof(RecordUnequipped)));
        try
        {
            await run();
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
        }
    }

    private static async Task CleanerUsesBroom(ScenarioContext context, bool on)
    {
        await WithSetting("ToolsSwitchForWork", on ? "true" : "false", async () =>
        {
            HomeArea(context);
            var cleaner = context.Worker(0, 0, WorkTypeDefOf.Cleaning);
            GiveKnife(cleaner);
            var broom = GiveTool(cleaner, "DA_MeleeWeapon_Broom");
            var cells = Enumerable.Range(2, 4).Select(x => context.Cell(x, 0)).ToList();
            foreach (var cell in cells)
            {
                GenSpawn.Spawn(ThingMaker.MakeThing(ThingDefOf.Filth_Dirt), cell, context.Map);
            }

            context.Check("the filth is there", cells.All(cell => cell.GetThingList(context.Map).Any(thing => thing.def.category == ThingCategory.Filth)));

            var cleaning = 0;
            var cleaningWith = 0;
            await context.Until($"the filth is cleaned: cleaner {Stage.Describe(cleaner)}, holding {Held(cleaner)}", () =>
            {
                if (cleaner.CurJobDef == JobDefOf.Clean && !IsWalking(cleaner))
                {
                    cleaning++;
                    cleaningWith += cleaner.equipment.Primary == broom ? 1 : 0;
                }

                return cells.All(cell => cell.GetThingList(context.Map).All(thing => thing.def.category != ThingCategory.Filth));
            }, 3000, 1);
            context.Check($"the cleaner cleaned: {cleaning} ticks", cleaning > 0);
            if (on)
            {
                context.Check($"the broom was out for the cleaning: {cleaningWith} of {cleaning} ticks", cleaningWith > cleaning * 0.9f);            }
            else
            {
                // Its weapon preference can still pick the broom as the best melee weapon, so the test is who equipped it
                var bySwitch = EquippedBy.Where(entry => entry.StartsWith("DA_MeleeWeapon_Broom") && (entry.Contains("ByStatModifiers") || entry.Contains("PreInit"))).ToList();
                context.Check($"Simple Sidearms' tool switching alone never takes the broom out for cleaning: [{string.Join(" || ", bySwitch)}]", bySwitch.Count == 0);
            }
        });
    }

    // 5, and the setting off. A miner (Mining at priority 1) fetches the pickaxe from a stockpile; a colonist with Mining at
    // priority 3 doesn't.
    private static async Task ToolsMinerFetches(ScenarioContext context, bool on)
    {
        await WithWorkPriorities(() => WithSetting("ToolsFetch", on ? "true" : "false", async () =>
        {
            Stockpile(context, 6, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var other = context.Worker(0, 4, WorkTypeDefOf.Mining);
            other.workSettings.SetPriority(WorkTypeDefOf.Mining, 3);
            Assignments(context).Refresh();
            if (!on)
            {
                context.Check("with the setting off, nobody is sent for it", Assignments(context).ToFetch(miner).Count == 0);
                await context.Step(600);
                context.Check($"and it stays in the stockpile: miner {Stage.Describe(miner)}", pickaxe.Spawned);
                return;
            }

            context.Check($"only the miner is sent for it: miner [{string.Join(", ", Assignments(context).ToFetch(miner).Select(tool => tool.def.defName))}], other [{string.Join(", ", Assignments(context).ToFetch(other).Select(tool => tool.def.defName))}]",
                Assignments(context).ToFetch(miner).Contains(pickaxe) && Assignments(context).ToFetch(other).Count == 0);
            await context.Until($"the miner picks it up: miner {Stage.Describe(miner)}, other {Stage.Describe(other)}", () => Carries(miner, pickaxe), 1500);
            context.Check("it's remembered as fetched", GameComponent_FetchedTools.IsFetched(pickaxe));
            context.Check("the other colonist didn't take it", !Carries(other, pickaxe));
        }));
    }

    // 6. Two miners and one pickaxe: the more skilled one gets it, and the other isn't sent for anything
    private static async Task ToolsSkilledMinerFirst(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var novice = context.Worker(0, 4, WorkTypeDefOf.Mining);
            var expert = context.Worker(0, 0, WorkTypeDefOf.Mining);
            novice.skills.GetSkill(SkillDefOf.Mining).Level = 3;
            expert.skills.GetSkill(SkillDefOf.Mining).Level = 15;
            Assignments(context).Refresh();
            context.Check("the expert is sent for it, the novice isn't", Assignments(context).ToFetch(expert).Contains(pickaxe) && Assignments(context).ToFetch(novice).Count == 0);
            await context.Until($"the expert picks it up: expert {Stage.Describe(expert)}", () => Carries(expert, pickaxe), 1500);
            Assignments(context).Refresh();
            context.Check("afterwards nobody has anything to fetch or hand back", !Assignments(context).HasWork(expert) && !Assignments(context).HasWork(novice));
        });
    }

    // 7. With Auto Arm, an outfit that bans the tools category: not sent, so no wasted walk
    private static async Task ToolsOutfitBans(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var was = miner.outfits.CurrentApparelPolicy;
            var policy = Current.Game.outfitDatabase.MakeNewOutfit();
            // Set up for weapons, as Auto Arm sets up a new game's policies, except tools
            policy.filter.SetAllow(ThingCategoryDefOf.Weapons, true);
            policy.filter.SetAllow(DefDatabase<ThingCategoryDef>.GetNamed("Ordo_Tools"), false);
            context.Check("the policy bans the pickaxe", !policy.filter.Allows(pickaxe.def));
            miner.outfits.CurrentApparelPolicy = policy;
            try
            {
                Assignments(context).Refresh();
                context.Check("the miner isn't sent for it", Assignments(context).ToFetch(miner).Count == 0);
                await context.Step(600);
                context.Check($"and doesn't take it: miner {Stage.Describe(miner)}", pickaxe.Spawned && !Carries(miner, pickaxe));
            }
            finally
            {
                miner.outfits.CurrentApparelPolicy = was;
                Current.Game.outfitDatabase.TryDelete(policy);
            }
        });
    }

    // 8. With Auto Arm, a fetched tool is a forced sidearm, so Auto Arm doesn't swap it for a weapon
    private static async Task ToolsAutoArmForced(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            Assignments(context).Refresh();
            await context.Until($"the miner picks it up: miner {Stage.Describe(miner)}", () => Carries(miner, pickaxe), 1500);
            var isForced = AccessTools.Method(AccessTools.TypeByName("AutoArm.ForcedWeapons"), "IsForced", new[] { typeof(Pawn), typeof(ThingWithComps) });
            context.Check("Auto Arm's IsForced was found", isForced != null);
            context.Check("Auto Arm treats it as forced", isForced != null && (bool)isForced.Invoke(null, new object[] { miner, pickaxe }));
        });
    }

    // 9. A fetched tool whose work leaves the main band stays for a day; with the work off, it's dropped in the home area for
    // haulers, and forgotten
    private static async Task ToolsHandBack(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            // No home area: handing back no longer needs one
            Stockpile(context, 6, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            Assignments(context).Refresh();
            await context.Until($"the miner picks it up: miner {Stage.Describe(miner)}", () => Carries(miner, pickaxe), 1500);

            miner.workSettings.SetPriority(WorkTypeDefOf.Mining, 4);
            Assignments(context).Refresh();
            context.Check("with mining just out of the main band, it's kept", Assignments(context).ToHandBack(miner).Count == 0);

            miner.workSettings.SetPriority(WorkTypeDefOf.Mining, 0);
            Assignments(context).Refresh();
            context.Check("with mining off, it's to be handed back", Assignments(context).ToHandBack(miner).Contains(pickaxe));
            await context.Until($"the miner carries it back to the stockpile: miner {Stage.Describe(miner)}, pickaxe at {pickaxe.PositionHeld}", () => !Carries(miner, pickaxe) && pickaxe.Spawned && pickaxe.IsInValidStorage(), 1500);
            context.Check("it's no longer remembered as fetched", !GameComponent_FetchedTools.IsFetched(pickaxe));
            context.Check("it's unforbidden", !pickaxe.IsForbidden(Faction.OfPlayer));
        });
    }

    // A tool the player gave is never handed back, whatever the pawn's work
    private static async Task ToolsPlayerToolKept(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            HomeArea(context);
            var hauler = context.Worker(0, 0, WorkTypeDefOf.Hauling);
            var pickaxe = GiveTool(hauler, "DA_MeleeWeapon_Pickaxe");
            Assignments(context).Refresh();
            context.Check("a tool the player gave isn't handed back", Assignments(context).ToHandBack(hauler).Count == 0);
            await context.Step(300);
            context.Check($"and stays carried: hauler {Stage.Describe(hauler)}", Carries(hauler, pickaxe));
        });
    }

    // Picks up a fetched tool the way the scenario's pawns fetch: walks over with Tweaks' job
    private static async Task<ThingWithComps> Fetched(ScenarioContext context, Pawn pawn, string defName, int x, int z)
    {
        var tool = PlaceTool(context, defName, x, z);
        Assignments(context).Refresh();
        await context.Until($"{pawn.LabelShort} fetches the {defName}: {Stage.Describe(pawn)}", () => Carries(pawn, tool), 1500);
        context.Check($"the {defName} is marked fetched", GameComponent_FetchedTools.IsFetched(tool));
        return tool;
    }

    // Out of the hand or the inventory, onto the ground
    private static void DropCarried(Pawn pawn, ThingWithComps tool)
    {
        if (pawn.equipment.Primary == tool)
        {
            pawn.equipment.TryDropEquipment(tool, out _, pawn.Position, forbid: false);
        }
        else
        {
            pawn.inventory.innerContainer.TryDrop(tool, pawn.Position, pawn.Map, ThingPlaceMode.Near, out _);
        }
    }

    // Simple Sidearms' memory of the pawn's weapons, for a failure message
    private static string MemoryState(Pawn pawn)
    {
        var memory = SidearmMemory(pawn);
        if (memory == null)
        {
            return "no memory";
        }

        string Field(string name) => AccessTools.Field(memory.GetType(), name)?.GetValue(memory)?.ToString() ?? "null";
        var remembered = AccessTools.Field(memory.GetType(), "rememberedWeapons")?.GetValue(memory) as System.Collections.IEnumerable;
        return $"mode {Field("primaryWeaponMode")}, melee {Field("preferredMeleeWeaponEx")}, ranged {Field("defaultRangedWeaponEx")}, forced {Field("forcedWeaponEx")}, remembered [{string.Join(", ", remembered?.Cast<object>() ?? Enumerable.Empty<object>())}]";
    }

    // Runs with Simple Sidearms' limits on one sidearm's weight off (its presets cap a melee sidearm well under the
    // jackhammer's 7 kg), restored afterwards
    private static async Task WithoutSidearmWeightLimit(Func<Task> run)
    {
        var settings = AccessTools.Property(AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms"), "Settings").GetValue(null);
        var fields = new[] { "LimitModeSingle", "LimitModeSingleMelee" }.Select(name => AccessTools.Field(settings.GetType(), name)).ToList();
        var was = fields.Select(field => field.GetValue(settings)).ToList();
        foreach (var field in fields)
        {
            field.SetValue(settings, Enum.Parse(field.FieldType, "None"));
        }

        try
        {
            await run();
        }
        finally
        {
            for (var i = 0; i < fields.Count; i++)
            {
                fields[i].SetValue(settings, was[i]);
            }
        }
    }

    // Simple Sidearms' reason for not letting the pawn pick it up, or null
    private static string PickupRefusal(Pawn pawn, ThingWithComps tool)
    {
        var method = AccessTools.Method(AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.StatCalculator"), "CanPickupSidearmInstance");
        var args = new object[] { tool, pawn, null };
        return (bool)method.Invoke(null, args) ? null : (string)args[2];
    }

    // As a pickup by any job: into the inventory, then Simple Sidearms told, as its equip job and Auto Arm's swap do
    private static void AddAsSidearm(Pawn pawn, ThingWithComps tool)
    {
        if (tool.Spawned)
        {
            tool.DeSpawn();
        }

        pawn.inventory.innerContainer.TryAdd(tool);
        var memory = SidearmMemory(pawn);
        AccessTools.Method(memory.GetType(), "InformOfAddedSidearm").Invoke(memory, new object[] { tool });
    }

    // Simple Sidearms' melee choice, for a pawn whose only melee weapon is a jackhammer (as when Auto Arm's policy enforcement
    // has dropped their knife): out of a fight, none, rather than idle and walk with it; drafted or with the setting off, the
    // jackhammer
    private static async Task ToolsNoJackhammerAsEverydayWeapon(ScenarioContext context)
    {
        PoliciesAllowWeapons();
        var pawn = context.Worker(0, 0, WorkTypeDefOf.Hauling);
        var jackhammer = GiveTool(pawn, "AdvancedTool_Jackhammer");
        var find = AccessTools.Method(AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.Utilities.GettersFilters"), "findBestMeleeWeapon");

        ThingWithComps Best()
        {
            var args = new object[] { pawn, null, true, false, null };
            find.Invoke(null, args);
            return args[1] as ThingWithComps;
        }

        context.Check($"out of a fight, none: {Best()?.def.defName ?? "none"}", Best() == null);
        await WithSetting("ToolsSwitchForWork", "false", () =>
        {
            context.Check($"with the setting off, Simple Sidearms' own choice: {Best()?.def.defName ?? "none"}", Best() == jackhammer);
            return Task.CompletedTask;
        });
        pawn.drafter.Drafted = true;
        context.Check($"drafted, its own choice too: {Best()?.def.defName ?? "none"}", Best() == jackhammer);
        pawn.drafter.Drafted = false;
    }

    // Fix 1. The expert may not leave an allowed area that leaves out the stockpile, so the pickaxe goes to the novice,
    // instead of to the expert at every refresh and never fetched
    private static async Task ToolsAreaRestricted(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 8, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 8, 0);
            var expert = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var novice = context.Worker(0, 4, WorkTypeDefOf.Mining);
            expert.skills.GetSkill(SkillDefOf.Mining).Level = 15;
            novice.skills.GetSkill(SkillDefOf.Mining).Level = 3;
            context.Map.areaManager.TryMakeNewAllowed(out var area);
            foreach (var cell in CellRect.CenteredOn(expert.Position, 2))
            {
                area[cell] = true;
            }

            expert.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            try
            {
                Assignments(context).Refresh();
                context.Check($"not the expert, who can't go there: [{string.Join(", ", Assignments(context).ToFetch(expert).Select(tool => tool.def.defName))}]", Assignments(context).ToFetch(expert).Count == 0);
                context.Check("the novice instead", Assignments(context).ToFetch(novice).Contains(pickaxe));
                await context.Until($"the novice picks it up: novice {Stage.Describe(novice)}", () => Carries(novice, pickaxe), 1500);
            }
            finally
            {
                expert.playerSettings.AreaRestrictionInPawnCurrentMap = null;
            }
        });
    }

    private static System.Reflection.FieldInfo SidearmsToolAutoSwitch(out object settings)
    {
        settings = AccessTools.Property(AccessTools.TypeByName("PeteTimesSix.SimpleSidearms.SimpleSidearms"), "Settings").GetValue(null);
        return AccessTools.Field(settings.GetType(), "ToolAutoSwitch");
    }

    // Fix 2. With Simple Sidearms' tool autoswitch off, tools would never come out, so nobody fetches
    private static async Task ToolsFetchNeedsAutoswitch(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var field = SidearmsToolAutoSwitch(out var settings);
            var was = field.GetValue(settings);
            field.SetValue(settings, false);
            try
            {
                Assignments(context).Refresh();
                context.Check("with Simple Sidearms' autoswitch off, the miner isn't sent", Assignments(context).ToFetch(miner).Count == 0);
                await context.Step(300);
                context.Check("and the pickaxe stays put", pickaxe.Spawned);
            }
            finally
            {
                field.SetValue(settings, was);
            }
        });
    }

    // Fix 4. Auto Arm's sidearm upgrade drops a fetched tool and adds a better copy: the copy is fetched in its place, so it
    // still goes back when the work ends
    private static async Task ToolsCopyStaysFetched(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var old = await Fetched(context, miner, "DA_MeleeWeapon_Pickaxe", 6, 0);
            Assignments(context).Refresh();
            DropCarried(miner, old);
            var copy = MakeWeapon("DA_MeleeWeapon_Pickaxe");
            PlacedTools.Add(copy);
            AddAsSidearm(miner, copy);
            context.Check("the copy is fetched", GameComponent_FetchedTools.IsFetched(copy));
            context.Check("the dropped one isn't", !GameComponent_FetchedTools.IsFetched(old));
        });
    }

    // Fix 7. A fetched tool picked up another way (here by a second colonist, as the player would order) is the player's
    private static async Task ToolsTakenFetchedIsPlayers(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var other = context.Worker(0, 4, WorkTypeDefOf.Hauling);
            var pickaxe = await Fetched(context, miner, "DA_MeleeWeapon_Pickaxe", 6, 0);
            DropCarried(miner, pickaxe);
            AddAsSidearm(other, pickaxe);
            context.Check("picked up by someone not sent for it, it's no longer fetched", !GameComponent_FetchedTools.IsFetched(pickaxe));
            Assignments(context).Refresh();
            context.Check("so it isn't handed back", Assignments(context).ToHandBack(other).Count == 0);
        });
    }

    // Fix 6. Side jobs of a tool's work type don't switch: feeding a patient is Doctor work but not doctoring
    private static Task ToolsSideJobsNoSwitch(ScenarioContext context)
    {
        var feed = JobMaker.MakeJob(JobDefOf.FeedPatient);
        feed.workGiverDef = DefDatabase<WorkGiverDef>.AllDefsListForReading.FirstOrDefault(giver => giver.workType == WorkTypeDefOf.Doctor);
        var stats = new HashSet<StatDef>();
        ToolStats.ForJob(feed, stats);
        context.Check($"feeding a patient uses no tool stats: [{string.Join(", ", stats.Select(stat => stat.defName))}]", stats.Count == 0);

        var tend = JobMaker.MakeJob(JobDefOf.TendPatient);
        stats.Clear();
        ToolStats.ForJob(tend, stats);
        context.Check($"tending does: [{string.Join(", ", stats.Select(stat => stat.defName))}]", stats.Contains(StatDefOf.MedicalTendQuality));
        return Task.CompletedTask;
    }

    // Fix 8. With no storage that takes the tool, it isn't handed back to lie on the floor
    private static async Task ToolsHandBackNeedsStorage(ScenarioContext context)
    {
        await WithWorkPriorities(async () =>
        {
            var zone = Stockpile(context, 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var pickaxe = await Fetched(context, miner, "DA_MeleeWeapon_Pickaxe", 6, 0);
            zone.Delete();
            miner.workSettings.SetPriority(WorkTypeDefOf.Mining, 0);
            Assignments(context).Refresh();
            await context.Step(300);
            context.Check($"with nowhere to store it, the miner keeps it: {Stage.Describe(miner)}", Carries(miner, pickaxe));
        });
    }

    // Fix 9. Work with no skill takes a tool slot only when skilled work leaves one: with mining, growing and cleaning all at
    // priority 1 and two slots, the pickaxe and the scythe, not the broom. Without manual priorities, a cleaner gets a broom.
    private static async Task ToolsSkilledWorkFirst(ScenarioContext context)
    {
        Stockpile(context, 6, 0);
        var pickaxe = PlaceTool(context, "DA_MeleeWeapon_Pickaxe", 5, 0);
        var scythe = PlaceTool(context, "DA_MeleeWeapon_Scythe", 6, 0);
        var broom = PlaceTool(context, "DA_MeleeWeapon_Broom", 7, 0);
        await WithWorkPriorities(async () =>
        {
            var worker = context.Worker(0, 0, WorkTypeDefOf.Mining, WorkTypeDefOf.Growing, WorkTypeDefOf.Cleaning);
            Assignments(context).Refresh();
            var fetch = Assignments(context).ToFetch(worker);
            context.Check($"the pickaxe and scythe, not the broom: [{string.Join(", ", fetch.Select(tool => tool.def.defName))}]", fetch.Contains(pickaxe) && fetch.Contains(scythe) && !fetch.Contains(broom));
            foreach (var workType in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                worker.workSettings.SetPriority(workType, 0);
            }

            await Task.CompletedTask;
        });

        var was = Find.PlaySettings.useWorkPriorities;
        Find.PlaySettings.useWorkPriorities = false;
        try
        {
            var cleaner = context.Worker(0, 0, WorkTypeDefOf.Cleaning);
            Assignments(context).Refresh();
            context.Check($"without manual priorities, a cleaner is sent for the broom: [{string.Join(", ", Assignments(context).ToFetch(cleaner).Select(tool => tool.def.defName))}]", Assignments(context).ToFetch(cleaner).Contains(broom));
        }
        finally
        {
            Find.PlaySettings.useWorkPriorities = was;
        }
    }

    // Fix 10. At the cap, a better tool still replaces a fetched one: with one slot, a miner with the pickaxe is sent for the
    // jackhammer and the pickaxe goes back
    private static async Task ToolsUpgradeAtCap(ScenarioContext context)
    {
        await WithSetting("ToolsPerPawn", "1", () => WithoutSidearmWeightLimit(() => WithWorkPriorities(async () =>
        {
            Stockpile(context, 6, 0);
            var miner = context.Worker(0, 0, WorkTypeDefOf.Mining);
            var pickaxe = await Fetched(context, miner, "DA_MeleeWeapon_Pickaxe", 6, 0);
            var jackhammer = PlaceTool(context, "AdvancedTool_Jackhammer", 6, 1);
            Assignments(context).Refresh();
            context.Check($"at the cap, the miner is sent for the jackhammer: [{string.Join(", ", Assignments(context).ToFetch(miner).Select(tool => tool.def.defName))}]; Simple Sidearms' refusal: {PickupRefusal(miner, jackhammer) ?? "none"}", Assignments(context).ToFetch(miner).Contains(jackhammer));
            context.Check("and the pickaxe is to go back", Assignments(context).ToHandBack(miner).Contains(pickaxe));
        })));
    }
}
