using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

public sealed class Scenario
{
	public readonly string Name;

	public readonly Func<ScenarioContext, Task> Run;

	// The scenario is skipped unless this ability is loaded, for abilities behind a DLC
	public readonly string RequiresAbility;

	// The features it covers, so that ordo/run_scenarios can run just the suite for one of them
	public readonly string[] Suites;

	public Scenario(
		string name,
		string suites,
		Func<ScenarioContext, Task> run,
		string requiresAbility = null
	)
	{
		Name = name;
		Suites = suites.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		Run = run;
		RequiresAbility = requiresAbility;
	}

	public bool InAnySuite(
		IReadOnlyCollection<string> suites
	)
	{
		return Suites.Any(suite => suites.Contains(suite, StringComparer.OrdinalIgnoreCase));
	}
}

public sealed class ScenarioRunner
{
	[Tool("ordo/list_scenarios", Description = "List each running mod's scenarios and the suites each belongs to, for choosing what to pass ordo/run_scenarios as suite")]
	public static object ListScenarios(
		[ToolParameter(Description = "Only this mod's scenarios, by packageId, name, folder or assembly name")] string mod = null
	)
	{
		var (sets, error) = ModTarget.Running(ScenarioSets.All, mod);
		if (error != null)
		{
			return new { success = false, message = error };
		}

		return new
		{
			success = true,
			mods = sets.Select(
				set => new
				{
					mod = set.Mod.PackageId,
					suites = set.Items
						.SelectMany(scenario => scenario.Suites)
						.Distinct()
						.OrderBy(suite => suite)
						.Select(suite => new { suite, scenarios = set.Items.Where(scenario => scenario.Suites.Contains(suite)).Select(scenario => scenario.Name) })
				}
			)
		};
	}

	// One running game is the fixture for every run: each scenario sets up its own site and cleans it up, so runs can follow one
	// another without restarting. Restart only to load a new build of the mod.
	[Tool("ordo/run_scenarios", Description = "Run the in-game scenarios of every running mod (or of mod) against the current map, cleaning up after each one, and report pass, fail or skip per scenario. Pass suite to test one feature; ordo/list_scenarios lists the suites. Removes every pawn on the map first and mutates the running game, so only use it on a test save and don't save afterwards.")]
	public static async Task<object> RunScenarios(
		IRimBridgeContext bridge,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Only run this mod's scenarios, by packageId, name, folder or assembly name")] string mod = null,
		[ToolParameter(Description = "Only run scenarios in these suites, separated by spaces or commas, such as \"charge knockback\"")] string suite = null,
		[ToolParameter(Description = "Only run scenarios whose name contains this")] string filter = null,
		[ToolParameter(Description = "Time every method of the mods under test with Dubs Performance Analyzer and report the totals. Needs DPA loaded and a window: without rendering DPA logs an error, which fails the scenarios.")] bool profile = false
	)
	{
		if (Find.CurrentMap == null)
		{
			return new { success = false, message = "No map is loaded" };
		}

		var (sets, setsError) = ModTarget.Running(ScenarioSets.All, mod);
		if (setsError != null)
		{
			return new { success = false, message = setsError };
		}

		ScenarioProfiler profiler = null;
		if (profile)
		{
			string error;
			(profiler, error) = await ScenarioProfiler.Start(bridge, sets.Select(set => set.Mod));
			if (profiler == null)
			{
				return new { success = false, message = error };
			}
		}

		var map = Find.CurrentMap;
		StageSites.ClearOfVisitors(map);

		var sites = StageSites.FindAndClear(map);
		if (sites.Count == 0)
		{
			return new { success = false, message = $"No clear {StageSites.Radius * 2}-cell site" };
		}

		var suites = suite?.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
		var scenarios = sets.SelectMany(set => set.Items)
			.Where(scenario => suites == null || scenario.InAnySuite(suites))
			.Where(scenario => filter == null || scenario.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
			.ToList();
		if (scenarios.Count == 0)
		{
			return new { success = false, message = "No scenario matches; ordo/list_scenarios lists the suites" };
		}

		var results = new List<object>();
		var failed = 0;
		var skipped = 0;
		var nextSite = 0;
		foreach (var scenario in scenarios)
		{
			if (scenario.RequiresAbility != null && DefDatabase<AbilityDef>.GetNamedSilentFail(scenario.RequiresAbility) == null)
			{
				skipped++;
				results.Add(new { scenario.Name, status = "skip", reason = $"{scenario.RequiresAbility} isn't loaded" });
				continue;
			}

			if (profiler != null)
			{
				await profiler.BeforeScenario();
			}

			var failures = await RunOne(scenario, bridge, map, sites[nextSite++ % sites.Count], cancellationToken);
			if (profiler != null)
			{
				await profiler.AfterScenario(scenario.Name);
			}

			if (failures.Count > 0)
			{
				failed++;
				results.Add(new { scenario.Name, status = "FAIL", failures });
			}
			else
			{
				results.Add(new { scenario.Name, status = "pass" });
			}
		}

		if (profiler != null)
		{
			await profiler.Stop();
		}

		return new
		{
			success = failed == 0,
			total = scenarios.Count,
			passed = scenarios.Count - failed - skipped,
			failed,
			skipped,
			siteCount = sites.Count,
			results,
			profile = profiler?.Report()
		};
	}

	private static async Task<List<string>> RunOne(
		Scenario scenario,
		IRimBridgeContext bridge,
		Map map,
		IntVec3 origin,
		CancellationToken cancellationToken
	)
	{
		var context = new ScenarioContext(bridge, map, origin, cancellationToken);
		var errorsBefore = LoggedErrorCount();
		try
		{
			await scenario.Run(context);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			context.Fail($"threw {exception.GetType().Name}: {exception.Message}");
		}

		try
		{
			await context.CleanUp();
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			context.Fail($"cleanup threw {exception.GetType().Name}: {exception.Message}");
		}

		var newErrors = LoggedErrorCount() - errorsBefore;
		if (newErrors > 0)
		{
			var latest = Log.Messages.Last(message => message.type == LogMessageType.Error).text;
			context.Fail($"logged {newErrors} error(s), the latest: {latest}");
		}

		return context.Failures;
	}

	// Counts repeats, because the log merges an error that's the same as the one before it into a single entry
	private static int LoggedErrorCount()
	{
		return Log.Messages.Where(message => message.type == LogMessageType.Error).Sum(message => message.repeats);
	}
}

public sealed class ScenarioContext : Stage
{
	public ScenarioContext(
		IRimBridgeContext bridge,
		Map map,
		IntVec3 origin,
		CancellationToken cancellationToken
	) : base(bridge, map, origin, cancellationToken)
	{
	}

	public override Task Step(
		int ticks
	)
	{
		return Bridge.Game.StepTicksAsync(ticks, null, CancellationToken);
	}

	public override Task CleanUp()
	{
		// A dialog nobody answered would pause the game and stop the cast it's guarding
		if (Find.WindowStack.WindowOfType<Dialog_MessageBox>() is { } dialog)
		{
			Fail($"left a dialog open: {dialog.text}");
			dialog.Close();
		}

		return base.CleanUp();
	}
}
