using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using Verse;

namespace Ordo.BridgeTools.Scenarios;

// Times every method of the mods under test with Dubs Performance Analyzer, through RimBridgeServer's dpa tools. DPA only
// keeps the last 2000 ticks, which is less than a full run, so this resets it before each scenario and adds up a snapshot taken
// after.
public sealed class ScenarioProfiler
{
	private readonly IRimBridgeContext _bridge;

	private readonly Dictionary<string, MethodTotals> _methods = new();

	private readonly List<ScenarioTotals> _scenarios = new();

	// What every method name starts with, left out of the report
	private readonly string _commonPrefix;

	private ScenarioProfiler(
		IRimBridgeContext bridge,
		string commonPrefix
	)
	{
		_bridge = bridge;
		_commonPrefix = commonPrefix;
	}

	// Null, with the reason, when DPA isn't loaded or wouldn't patch the mods
	public static async Task<(ScenarioProfiler Profiler, string Error)> Start(
		IRimBridgeContext bridge,
		IEnumerable<ModContentPack> mods
	)
	{
		var targets = mods
			.Where(mod => mod.assemblies.loadedAssemblies.Count > 0)
			.SelectMany(mod => ProfilingTools.Targets(ModTarget.MainAssembly(mod)))
			.ToList();
		if (targets.Count == 0)
		{
			return (null, "The mods under test have no code to profile");
		}

		var patch = await bridge.Tools.CallAsync<PatchResult>(
			"rimworld/dpa_patch_methods",
			new { category = "Tick", inputMode = "Method", targets = string.Join(";", targets), previewLimit = 0 }
		);
		if (!patch.Succeeded() || patch.Result == null || !patch.Result.success)
		{
			return (null, $"Dubs Performance Analyzer couldn't patch the mods: {patch.Error?.ToString() ?? patch.Result?.message ?? "no reason given"}");
		}

		return (new ScenarioProfiler(bridge, CommonNamespace(targets)), null);
	}

	public Task BeforeScenario()
	{
		return _bridge.Tools.CallAsync("rimworld/dpa_reset", new { });
	}

	public async Task AfterScenario(
		string scenario
	)
	{
		var snapshot = await _bridge.Tools.CallAsync<SnapshotResult>("rimworld/dpa_snapshot", new { sortBy = "total", limit = 500 });
		var rows = snapshot.Result?.rows ?? new List<SnapshotRow>();
		var scenarioTotals = new ScenarioTotals { scenario = scenario };
		foreach (var row in rows.Where(row => row.totalCalls > 0))
		{
			if (!_methods.TryGetValue(row.key, out var method))
			{
				method = new MethodTotals { method = ShortName(row.key) };
				_methods[row.key] = method;
			}

			method.calls += row.totalCalls;
			method.totalMs += row.totalMilliseconds;
			if (row.maxMilliseconds > method.maxMs)
			{
				method.maxMs = row.maxMilliseconds;
				method.slowestIn = scenario;
			}

			scenarioTotals.totalMs += row.totalMilliseconds;
			if (row.totalMilliseconds > scenarioTotals.topMs)
			{
				scenarioTotals.topMs = row.totalMilliseconds;
				scenarioTotals.topMethod = ShortName(row.key);
			}
		}

		_scenarios.Add(scenarioTotals);
	}

	// Removes the profiling patches again
	public Task Stop()
	{
		return _bridge.Tools.CallAsync("rimworld/dpa_cleanup", new { });
	}

	public object Report()
	{
		return new
		{
			totalMs = Round(_methods.Values.Sum(method => method.totalMs)),
			methods = _methods.Values
				.OrderByDescending(method => method.totalMs)
				.Select(method => new { method.method, method.calls, totalMs = Round(method.totalMs), averageMs = Round(method.totalMs / method.calls), maxMs = Round(method.maxMs), method.slowestIn }),
			scenarios = _scenarios
				.OrderByDescending(scenario => scenario.totalMs)
				.Select(scenario => new { scenario.scenario, totalMs = Round(scenario.totalMs), scenario.topMethod, topMs = Round(scenario.topMs) })
		};
	}

	private string ShortName(
		string key
	)
	{
		return key.StartsWith(_commonPrefix) ? key.Substring(_commonPrefix.Length) : key;
	}

	// The namespace every target shares, such as "Ordo.SillyAnimalThings.", cut at a dot
	private static string CommonNamespace(
		List<string> targets
	)
	{
		var prefix = targets[0].Substring(0, targets[0].LastIndexOf('.', targets[0].IndexOf(':')) + 1);
		foreach (var target in targets)
		{
			while (prefix.Length > 0 && !target.StartsWith(prefix))
			{
				prefix = prefix.Substring(0, prefix.LastIndexOf('.', prefix.Length - 2) + 1);
			}
		}

		return prefix;
	}

	private static double Round(
		double milliseconds
	)
	{
		return System.Math.Round(milliseconds, 4);
	}

	private sealed class MethodTotals
	{
		public string method;

		public int calls;

		public double totalMs;

		public double maxMs;

		public string slowestIn;
	}

	private sealed class ScenarioTotals
	{
		public string scenario;

		public double totalMs;

		public string topMethod;

		public double topMs;
	}

	// The parts of RimBridgeServer's dpa tool results this reads
	public sealed class PatchResult
	{
		public bool success;

		public string message;
	}

	public sealed class SnapshotResult
	{
		public List<SnapshotRow> rows;
	}

	public sealed class SnapshotRow
	{
		public string key;

		public int totalCalls;

		public double totalMilliseconds;

		public double maxMilliseconds;
	}
}
