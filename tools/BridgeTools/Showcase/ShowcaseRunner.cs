using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RimBridgeServer.Sdk;
using RimWorld;
using Verse;

namespace Ordo.BridgeTools.Showcase;

// How much of the 1080p window a clip's video shows
public enum ShowcaseFrame
{
	// The middle 1280 by 720 pixels at full size: the map around the stage, and a centred dialog
	Middle,

	// The whole window scaled down to 1280 by 720, for clips that need the UI at the window's edges, such as main tabs and
	// the inspect pane
	Window
}

// A staged demonstration. An ability clip is captioned from the ability's own def, or from the clip's own caption when it shows
// how the AI uses it. A feature clip, for anything that isn't an ability, brings its own title and caption.
public sealed class ShowcaseClip
{
	public readonly string Name;

	// Null for a feature clip
	public readonly string AbilityDef;

	public readonly string AnimalKind;

	public readonly Func<ShowcaseStage, Task> Run;

	// Replaces the ability's description, for clips about when an animal uses it rather than what it does
	public readonly string Caption;

	// A feature clip's title. Ability clips are titled from the ability and the animal.
	public readonly string Title;

	// Keep the game's UI on screen, for clips that show it. The UI is hidden otherwise.
	public readonly bool ShowUi;

	public readonly ShowcaseFrame Frame;

	public ShowcaseClip(
		string name,
		string abilityDef,
		string animalKind,
		Func<ShowcaseStage, Task> run,
		string caption = null
	)
	{
		Name = name;
		AbilityDef = abilityDef;
		AnimalKind = animalKind;
		Run = run;
		Caption = caption;
	}

	private ShowcaseClip(
		string name,
		string title,
		string caption,
		Func<ShowcaseStage, Task> run,
		bool showUi,
		ShowcaseFrame frame
	)
	{
		Name = name;
		Title = title;
		Caption = caption;
		Run = run;
		ShowUi = showUi;
		Frame = frame;
	}

	// A clip of something other than an ability, such as a dialog or a building at work
	public static ShowcaseClip Feature(
		string name,
		string title,
		string caption,
		Func<ShowcaseStage, Task> run,
		bool showUi = false,
		ShowcaseFrame frame = ShowcaseFrame.Middle
	)
	{
		return new ShowcaseClip(name, title, caption, run, showUi, frame);
	}
}

public sealed class ShowcaseRunner
{
	// Abilities can roll badly, and a clip where nothing happens isn't worth keeping
	private const int MaxTakes = 3;

	// What fits across a 1280-wide video at the caption's font size
	private const int MaxCaptionLength = 130;

	[Tool("ordo/record_showcase", Description = "Play a staged clip of each ability at normal speed while recording the game window and its sound, for make_showcase.ps1 to cut into captioned videos and a highlight reel. Records one mod's clips: pass mod when more than one running mod has some. Needs a showcase profile (such as sillyanimalthings-showcase), with its window visible and not minimized. Removes every pawn on the map first and mutates the running game, so only use it on a test save and don't save afterwards. This isn't a test: use ordo/run_scenarios for that.")]
	public static async Task<object> RecordShowcase(
		IRimBridgeContext bridge,
		CancellationToken cancellationToken,
		[ToolParameter(Description = "Absolute folder to record into. Replaces whatever was recorded there before, unless append is set.")] string outputDirectory,
		[ToolParameter(Description = "Absolute path of the built ProcessAudioCapture.exe")] string audioCapturePath,
		[ToolParameter(Description = "Only record clips whose name contains this")] string filter = null,
		[ToolParameter(Description = "The mod whose clips to record, by packageId, name, folder or assembly name")] string mod = null,
		[ToolParameter(Description = "Keep the folder's other clip videos and replace only the clips recorded now, so make_showcase.ps1 builds the reel from all of them. Refused while an earlier recording there hasn't been cut yet.")] bool append = false
	)
	{
		if (append && File.Exists(Path.Combine(outputDirectory, "timeline.tsv")))
		{
			return new { success = false, message = $"{outputDirectory} holds a recording that hasn't been cut yet: run make_showcase.ps1 on it first, or record without append" };
		}

		if (Find.CurrentMap == null)
		{
			return new { success = false, message = "No map is loaded" };
		}

		var (sets, error) = ModTarget.Running(ShowcaseSets.All, mod);
		if (error != null || sets.Count > 1)
		{
			return new { success = false, message = error ?? $"Pass mod: more than one running mod has clips ({string.Join(", ", sets.Select(set => set.Mod.PackageId))})" };
		}

		var allClips = sets[0].Items;
		var map = Find.CurrentMap;
		StageSites.ClearOfVisitors(map);

		var sites = StageSites.FindAndClear(map);
		if (sites.Count == 0)
		{
			return new { success = false, message = $"No clear {StageSites.Radius * 2}-cell site" };
		}

		SetTheScene(map);
		PrepareOutput(outputDirectory, append);

		var clips = allClips
			.Where(clip => filter == null || clip.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
			.ToList();

		var results = new List<object>();
		var timeline = new StringBuilder();
		var unfinished = 0;
		var nextSite = 0;
		var screenshotMode = Find.UIRoot.screenshotMode;
		var wasHidingUi = screenshotMode.active;
		screenshotMode.active = true;

		ShowcaseRecording recording;
		try
		{
			recording = ShowcaseRecording.Start(outputDirectory, audioCapturePath);
		}
		catch (Exception exception) when (exception is InvalidOperationException or FileNotFoundException or System.ComponentModel.Win32Exception)
		{
			screenshotMode.active = wasHidingUi;
			return new { success = false, message = exception.Message };
		}

		string recordingProblem;
		try
		{
			await recording.WaitForVideo(bridge, cancellationToken);
			foreach (var clip in clips)
			{
				var ability = clip.AbilityDef == null ? null : DefDatabase<AbilityDef>.GetNamedSilentFail(clip.AbilityDef);
				if (clip.AbilityDef != null && ability == null)
				{
					results.Add(new { clip.Name, status = "skip", reason = $"{clip.AbilityDef} isn't loaded" });
					continue;
				}

				(int Takes, long Start, long End, List<string> Failures) outcome;
				var adaptiveTraining = Prefs.AdaptiveTrainingEnabled;
				screenshotMode.active = !clip.ShowUi;
				try
				{
					// The learning helper's lessons would sit over the shot, and so would messages about the last clip's cleanup
					if (clip.ShowUi)
					{
						Prefs.AdaptiveTrainingEnabled = false;
						Messages.Clear();
					}

					outcome = await PerformUntilItWorks(clip, bridge, map, sites, () => nextSite++, cancellationToken);
				}
				finally
				{
					screenshotMode.active = true;
					Prefs.AdaptiveTrainingEnabled = adaptiveTraining;
				}

				if (outcome.Failures.Count > 0)
				{
					unfinished++;
					results.Add(new { clip.Name, status = "didn't work", takes = outcome.Takes, outcome.Failures });
					continue;
				}

				// Numbered so the reel plays in the order they're listed
				var clipName = $"{allClips.IndexOf(clip) + 1:D2}_{clip.Name}";
				var (title, caption) = ability != null
					? AbilityCaption(ability, clip.AnimalKind, clip.Caption)
					: (clip.Title, clip.Caption);
				WriteCaption(Path.Combine(outputDirectory, "clips", clipName), title, caption);
				timeline.AppendLine($"{clipName}\t{outcome.Start}\t{outcome.End}\t{(clip.Frame == ShowcaseFrame.Window ? "window" : "middle")}");
				results.Add(new { clip.Name, status = "recorded", takes = outcome.Takes, seconds = (outcome.End - outcome.Start) / 1000.0 });
			}
		}
		finally
		{
			recordingProblem = await recording.Stop(bridge);
			screenshotMode.active = wasHidingUi;
			Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
			File.WriteAllText(Path.Combine(outputDirectory, "timeline.tsv"), timeline.ToString());
		}

		return new { success = unfinished == 0 && recordingProblem == null, recordingProblem, results };
	}

	// When appending, the earlier videos stay. make_showcase.ps1 overwrites a clip's video only if it was recorded again
	// successfully, so a clip that fails to re-record keeps its old one.
	private static void PrepareOutput(
		string outputDirectory,
		bool append
	)
	{
		Directory.CreateDirectory(outputDirectory);
		if (!append)
		{
			foreach (var file in Directory.GetFiles(outputDirectory, "*.mp4"))
			{
				File.Delete(file);
			}
		}

		var clipsDirectory = Path.Combine(outputDirectory, "clips");
		if (Directory.Exists(clipsDirectory))
		{
			Directory.Delete(clipsDirectory, true);
		}
	}

	// Daylight, and no rain to put out the fires
	private static void SetTheScene(
		Map map
	)
	{
		ColonyTools.SetHour(12);
		map.weatherManager.curWeather = WeatherDefOf.Clear;
		map.weatherManager.lastWeather = WeatherDefOf.Clear;
	}

	private static async Task<(int Takes, long Start, long End, List<string> Failures)> PerformUntilItWorks(
		ShowcaseClip clip,
		IRimBridgeContext bridge,
		Map map,
		List<IntVec3> sites,
		Func<int> nextSite,
		CancellationToken cancellationToken
	)
	{
		for (var take = 1; ; take++)
		{
			var stage = new ShowcaseStage(bridge, map, sites[nextSite() % sites.Count], cancellationToken);
			stage.Frame();
			try
			{
				await clip.Run(stage);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				stage.Fail($"threw {exception.GetType().Name}: {exception.Message}");
			}

			var end = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			await stage.CleanUp();
			await stage.LetTheSoundDie();
			if (stage.Failures.Count == 0 || take == MaxTakes)
			{
				return (take, stage.StartedAt, end, stage.Failures);
			}
		}
	}

	// The ability's description, so the video says what the mod says
	private static (string Title, string Caption) AbilityCaption(
		AbilityDef ability,
		string animalKind,
		string captionOverride
	)
	{
		var title = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(ability.label);
		var animal = DefDatabase<PawnKindDef>.GetNamed(animalKind).race.label;
		var animalName = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(animal);
		return ($"{title}  |  {animalName}", captionOverride ?? ability.description);
	}

	// As much of the caption as fits on a line, in whole sentences
	private static void WriteCaption(
		string clipDirectory,
		string title,
		string fullCaption
	)
	{
		var caption = "";
		foreach (var sentence in fullCaption.Split(". "))
		{
			var longer = caption.Length == 0 ? sentence : $"{caption}. {sentence}";
			if (caption.Length > 0 && longer.Length > MaxCaptionLength)
			{
				break;
			}

			caption = longer;
		}

		Directory.CreateDirectory(clipDirectory);
		File.WriteAllText(Path.Combine(clipDirectory, "title.txt"), title);
		File.WriteAllText(Path.Combine(clipDirectory, "caption.txt"), caption.TrimEnd('.') + ".");
	}
}

// A stage played at normal speed in front of the camera. The recording is what films it.
public sealed class ShowcaseStage : Stage
{
	// Close enough to see the animals, and as close as the game's camera goes. In a 1080p window this puts 26 by 15 cells in
	// the middle 1280 by 720 pixels, which is what gets recorded.
	private const float CameraSize = 11f;

	private const int BeatTicks = 45;

	private const int LingerTicks = 120;

	private const int SoundDieTicks = 90;

	public ShowcaseStage(
		IRimBridgeContext bridge,
		Map map,
		IntVec3 origin,
		CancellationToken cancellationToken
	) : base(bridge, map, origin, cancellationToken)
	{
	}

	public long StartedAt { get; private set; }

	public void Frame()
	{
		Find.CameraDriver.SetRootPosAndSize(Origin.ToVector3Shifted(), CameraSize);
	}

	public override async Task Step(
		int ticks
	)
	{
		if (StartedAt == 0)
		{
			StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}

		// Cleaning up after the last stage leaves the game paused
		Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
		var target = Find.TickManager.TicksGame + ticks;
		while (Find.TickManager.TicksGame < target)
		{
			await Bridge.Game.NextFrameAsync(CancellationToken);
		}
	}

	// Waits in real time rather than game time, for clips where a dialog pauses the game
	public async Task Hold(
		float seconds
	)
	{
		if (StartedAt == 0)
		{
			StartedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
		}

		var clock = System.Diagnostics.Stopwatch.StartNew();
		while (clock.Elapsed.TotalSeconds < seconds)
		{
			await Bridge.Game.NextFrameAsync(CancellationToken);
		}
	}

	// Long enough to see who's in the shot before anything happens
	public Task Beat()
	{
		return Step(BeatTicks);
	}

	// Long enough to see how it turned out
	public Task Linger(
		int ticks = LingerTicks
	)
	{
		return Step(ticks);
	}

	// So that the end of an explosion doesn't play over the start of the next clip
	public Task LetTheSoundDie()
	{
		return Step(SoundDieTicks);
	}
}

// One continuous recording of the game window and its sound. make_showcase.ps1 cuts it up afterwards, so clips stay in sync
// and what happens between them doesn't matter.
public sealed class ShowcaseRecording
{
	private const int VideoWarmUpMilliseconds = 2000;

	private const int StopTimeoutMilliseconds = 15000;

	private readonly System.Diagnostics.Process _video;

	private readonly System.Diagnostics.Process _audio;

	private ShowcaseRecording(
		System.Diagnostics.Process video,
		System.Diagnostics.Process audio
	)
	{
		_video = video;
		_audio = audio;
	}

	public static ShowcaseRecording Start(
		string outputDirectory,
		string audioCapturePath
	)
	{
		if (!File.Exists(audioCapturePath))
		{
			throw new FileNotFoundException($"Build tools/ProcessAudioCapture first: no {audioCapturePath}");
		}

		var videoPath = Path.Combine(outputDirectory, "capture.mkv");

		// The whole window: make_showcase.ps1 crops or scales each clip to its frame. Stamps each frame with the time, so the
		// script can line it up with the sound and the clips' times.
		var capture = "gfxcapture=window_exe=RimWorld\\.exe:capture_cursor=0:max_framerate=60,hwdownload,format=bgra,settb=1/1000,setpts=RTCTIME/1000";
		var video = StartProcess(FindFfmpeg(), $"-hide_banner -loglevel error -y -f lavfi -i \"{capture}\" -copyts -fps_mode passthrough -c:v libx264 -preset ultrafast -crf 14 -pix_fmt yuv420p \"{videoPath}\"");
		video.ErrorDataReceived += (_, _) => { };
		video.BeginErrorReadLine();

		var audio = StartProcess(audioCapturePath, $"{System.Diagnostics.Process.GetCurrentProcess().Id} \"{Path.Combine(outputDirectory, "capture.wav")}\"");
		var started = audio.StandardOutput.ReadLine();
		if (started == null || !started.StartsWith("started "))
		{
			video.StandardInput.WriteLine("q");
			throw new InvalidOperationException($"The audio capture didn't start: {audio.StandardError.ReadToEnd()}");
		}

		audio.BeginErrorReadLine();

		File.WriteAllText(Path.Combine(outputDirectory, "capture_audio_start.txt"), started.Substring("started ".Length));
		return new ShowcaseRecording(video, audio);
	}

	// ffmpeg gives no sign of its first frame, and writes its file before that, so this just allows plenty of time. The game
	// has to keep running meanwhile, because the capture only gets frames when the window changes.
	public async Task WaitForVideo(
		IRimBridgeContext bridge,
		CancellationToken cancellationToken
	)
	{
		Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
		var clock = System.Diagnostics.Stopwatch.StartNew();
		while (clock.ElapsedMilliseconds < VideoWarmUpMilliseconds && !_video.HasExited)
		{
			await bridge.Game.NextFrameAsync(cancellationToken);
		}
	}

	// Returns what went wrong, if anything. The game has to keep drawing while this waits, because ffmpeg can't stop until the
	// capture gets another frame, so blocking here would hold it up until a timeout.
	public async Task<string> Stop(
		IRimBridgeContext bridge
	)
	{
		var videoFailed = _video.HasExited;
		if (!videoFailed)
		{
			_video.StandardInput.WriteLine("q");
		}

		if (!_audio.HasExited)
		{
			_audio.StandardInput.WriteLine();
		}

		var clock = System.Diagnostics.Stopwatch.StartNew();
		while (clock.ElapsedMilliseconds < StopTimeoutMilliseconds && !(_video.HasExited && _audio.HasExited))
		{
			await bridge.Game.NextFrameAsync(CancellationToken.None);
		}

		if (videoFailed)
		{
			return $"ffmpeg stopped early with exit code {_video.ExitCode}";
		}

		return _video.HasExited && _audio.HasExited ? null : "The recorders didn't stop in time, so the recording may be incomplete";
	}

	private static System.Diagnostics.Process StartProcess(
		string path,
		string arguments
	)
	{
		return System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path, arguments)
		{
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		});
	}

	// The game may have been started before ffmpeg was installed, so its own PATH can't be relied on
	private static string FindFfmpeg()
	{
		var paths = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) + ";" + Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine);
		return paths.Split(';', StringSplitOptions.RemoveEmptyEntries)
			       .Select(directory => Path.Combine(directory, "ffmpeg.exe"))
			       .FirstOrDefault(File.Exists)
		       ?? throw new FileNotFoundException("ffmpeg isn't on the PATH: winget install Gyan.FFmpeg");
	}
}
