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

	public Scenario(
		string name,
		Func<ScenarioContext, Task> run,
		string requiresAbility = null
	)
	{
		Name = name;
		Run = run;
		RequiresAbility = requiresAbility;
	}
}

public sealed class ScenarioRunner
{
	[Tool("ordo/run_scenarios", Description = "Run the in-game scenarios against the current map, cleaning up after each one, and report pass, fail or skip per scenario. Removes every pawn on the map first and mutates the running game, so only use it on a test save and don't save afterwards.")]
	public static async Task<object> RunScenarios(
		IRimBridgeContext bridge,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Only run scenarios whose name contains this")] string filter = null
	)
	{
		if (Find.CurrentMap == null)
		{
			return new { success = false, message = "No map is loaded" };
		}

		var map = Find.CurrentMap;
		StageSites.ClearOfVisitors(map);

		var sites = StageSites.FindAndClear(map);
		if (sites.Count == 0)
		{
			return new { success = false, message = $"No clear {StageSites.Radius * 2}-cell site" };
		}

		var scenarios = SillyAnimalThingsScenarios.All
			.Where(scenario => filter == null || scenario.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
			.ToList();

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

			var failures = await RunOne(scenario, bridge, map, sites[nextSite++ % sites.Count], cancellationToken);
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

		return new
		{
			success = failed == 0,
			total = scenarios.Count,
			passed = scenarios.Count - failed - skipped,
			failed,
			skipped,
			siteCount = sites.Count,
			results
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
