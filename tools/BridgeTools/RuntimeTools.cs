using System;
using System.Collections;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using Verse;

namespace Ordo.BridgeTools;

// Diagnostics for reloading without a restart: what the runtime allows, how the game loaded the mod, and what the game's
// loading queue is waiting on
public sealed class RuntimeTools
{
	private const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

	private const string TargetDescription = ModTarget.ParameterDescription + " BridgeTools targets these bridge tools themselves.";

	// A mod's assembly, or this one: the bridge tools aren't a mod, but hot reload works on them the same way
	private static (Assembly Assembly, string Error) FindTarget(
		string mod
	)
	{
		var self = typeof(RuntimeTools).Assembly;
		if (mod != null && (string.Equals(mod.Trim(), "BridgeTools", StringComparison.OrdinalIgnoreCase) || string.Equals(mod.Trim(), self.GetName().Name, StringComparison.OrdinalIgnoreCase)))
		{
			return (self, null);
		}

		return ModTarget.FindAssembly(mod);
	}

	private const BindingFlags AnyInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

	[Tool("ordo/runtime_info", Description = "Report the .NET runtime, whether it allows hot reload, and how the game loaded a mod's assembly")]
	public static object RuntimeInfo(
		[ToolParameter(Description = TargetDescription)] string mod = null
	)
	{
		var (assembly, error) = FindTarget(mod);
		if (assembly == null)
		{
			return new { success = false, message = error };
		}

		return RuntimeReport(assembly);
	}

	private static object RuntimeReport(
		Assembly mod
	)
	{
		var debuggable = mod.GetCustomAttribute<DebuggableAttribute>();
		var context = AssemblyLoadContext.GetLoadContext(mod);
		return new
		{
			success = true,
			framework = RuntimeInformation.FrameworkDescription,
			version = Environment.Version.ToString(),
			metadataUpdaterSupported = MetadataUpdater.IsSupported,
			modifiableAssemblies = Environment.GetEnvironmentVariable("DOTNET_MODIFIABLE_ASSEMBLIES"),
			startupHooks = Environment.GetEnvironmentVariable("DOTNET_STARTUP_HOOKS"),
			debuggerAttached = Debugger.IsAttached,
			mod = new
			{
				name = mod.FullName,
				location = mod.Location,
				isDynamic = mod.IsDynamic,
				loadContext = context?.Name,
				loadContextType = context?.GetType().FullName,
				collectible = context?.IsCollectible,
				jitTracking = debuggable?.IsJITTrackingEnabled,
				jitOptimizerDisabled = debuggable?.IsJITOptimizerDisabled
			},
			loadContexts = AssemblyLoadContext.All.Select(all => new { all.Name, type = all.GetType().FullName, assemblies = all.Assemblies.Count() })
		};
	}

	[Tool("ordo/long_events", Description = "Show the state of the game's long event (loading) queue, read by reflection. markDisplayed marks the current event as shown on screen, which a synchronous event waits for.")]
	public static object LongEvents(
		[ToolParameter(Description = "Mark the current event as displayed so a synchronous event can run without a window")] bool markDisplayed = false
	)
	{
		var handler = typeof(LongEventHandler);
		var statics = handler.GetFields(AnyStatic).ToDictionary(field => field.Name, field => Describe(field.GetValue(null)));

		var current = handler.GetField("currentEvent", AnyStatic)?.GetValue(null);
		object currentFields = null;
		if (current != null)
		{
			currentFields = current.GetType().GetFields(AnyInstance).ToDictionary(field => field.Name, field => Describe(field.GetValue(current)));
			if (markDisplayed)
			{
				current.GetType().GetField("alreadyDisplayed", AnyInstance)?.SetValue(current, true);
			}
		}

		return new { success = true, statics, currentEvent = currentFields, marked = markDisplayed && current != null };
	}

	// A synchronous long event only runs once its loading screen has been drawn, and without a window nothing is drawn, so this
	// marks each one as drawn. With a window the game does that itself.
	[Tool("ordo/hot_reload_defs", Description = "Reload every def from XML without restarting, as dev mode's Hot reload Defs does, and wait for it to finish. Works without a window too. For C# changes use hot_reload.ps1 and ordo/apply_update.")]
	public static async Task<object> HotReloadDefs(
		IRimBridgeContext bridge,
		CancellationToken cancellationToken
	)
	{
		var reload = typeof(PlayDataLoader).GetMethod("HotReloadDefs", AnyStatic);
		if (reload == null)
		{
			return new { success = false, message = "PlayDataLoader.HotReloadDefs isn't in this version of the game" };
		}

		var clock = Stopwatch.StartNew();
		reload.Invoke(null, null);

		var displayed = typeof(LongEventHandler).GetNestedType("QueuedLongEvent", BindingFlags.NonPublic)?.GetField("alreadyDisplayed", AnyInstance);
		var currentEvent = typeof(LongEventHandler).GetField("currentEvent", AnyStatic);
		var steps = 0;
		while (LongEventHandler.AnyEventNowOrWaiting)
		{
			if (clock.Elapsed > TimeSpan.FromMinutes(2))
			{
				return new { success = false, message = "The reload didn't finish within 2 minutes", steps };
			}

			if (currentEvent?.GetValue(null) is { } current && displayed?.GetValue(current) is false)
			{
				displayed.SetValue(current, true);
				steps++;
			}

			await bridge.Game.NextFrameAsync(cancellationToken);
		}

		return new { success = true, steps, milliseconds = clock.ElapsedMilliseconds, defs = DefDatabase<ThingDef>.DefCount };
	}

	// The runtime half of .NET hot reload: applies metadata, IL and PDB deltas produced by Roslyn's EmitDifference to the mod's
	// loaded assembly. With no paths it applies empty deltas, which only tests whether the runtime lets the mod be edited.
	[Tool("ordo/apply_update", Description = "Apply a .NET hot reload delta made by tools/HotReloadDelta/hot_reload.ps1 to a mod's loaded assembly, or to these bridge tools with mod BridgeTools. Needs a Debug build and no debugger attached. New tools, and changes to a tool's parameters, still need a restart.")]
	public static object ApplyUpdate(
		[ToolParameter(Description = "Folder holding delta.meta, delta.il and delta.pdb")] string directory = null,
		[ToolParameter(Description = "Path to the metadata delta, instead of directory")] string metadataPath = null,
		[ToolParameter(Description = "Path to the IL delta, instead of directory")] string ilPath = null,
		[ToolParameter(Description = "Path to the PDB delta, instead of directory")] string pdbPath = null,
		[ToolParameter(Description = TargetDescription)] string mod = null
	)
	{
		if (directory != null)
		{
			metadataPath = System.IO.Path.Combine(directory, "delta.meta");
			ilPath = System.IO.Path.Combine(directory, "delta.il");
			pdbPath = System.IO.Path.Combine(directory, "delta.pdb");
		}

		var (assembly, error) = FindTarget(mod);
		if (assembly == null)
		{
			return new { success = false, message = error };
		}

		try
		{
			MetadataUpdater.ApplyUpdate(
				assembly,
				metadataPath == null ? ReadOnlySpan<byte>.Empty : System.IO.File.ReadAllBytes(metadataPath),
				ilPath == null ? ReadOnlySpan<byte>.Empty : System.IO.File.ReadAllBytes(ilPath),
				pdbPath == null ? ReadOnlySpan<byte>.Empty : System.IO.File.ReadAllBytes(pdbPath)
			);
			return new { success = true, mod = assembly.GetName().Name, applied = metadataPath != null };
		}
		catch (Exception exception)
		{
			return new { success = false, error = exception.GetType().Name, message = exception.Message };
		}
	}

	[Tool("ordo/ability_def", Description = "Read an ability def's live values, for checking that a def hot reload took effect")]
	public static object AbilityDefValues(
		[ToolParameter(Description = "The ability's defName")] string defName
	)
	{
		var def = DefDatabase<RimWorld.AbilityDef>.GetNamedSilentFail(defName);
		return def == null
			? new { success = false, message = $"No ability named {defName}" }
			: new { success = true, def.defName, def.label, range = def.verbProperties?.range, warmup = def.verbProperties?.warmupTime, cooldown = def.cooldownTicksRange.ToString() };
	}

	private static string Describe(
		object value
	)
	{
		return value switch
		{
			null => "null",
			string text => text,
			ICollection collection => $"{value.GetType().Name} ({collection.Count})",
			Delegate action => $"{action.Method.DeclaringType?.Name}.{action.Method.Name}",
			_ => value.ToString()
		};
	}
}
