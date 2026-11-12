# Recording a showcase

`tools/BridgeTools/Showcase` records a staged clip of each ability at normal speed with the game's sound, cut into captioned 60 fps MP4s and a highlight reel. It's separate from the tests: the clips' checks only decide whether a take is worth keeping, and a clip gets up to 3 takes. The two share only `Stage.cs`.

## Recording

1. Install ffmpeg (`winget install Gyan.FFmpeg`). Build the audio recorder once: `dotnet build tools/ProcessAudioCapture/ProcessAudioCapture.csproj -c Release`.
2. Start the mod's showcase profile (e.g. `sillyanimalthings-showcase`): a 1080p window, no music, and `runInBackground` on so that it keeps playing sound without focus. Don't minimize the window. The game's sound plays out loud while recording.
3. Call `ordo/record_showcase` with:
   - `outputDirectory`: the absolute path of `tools/BridgeTools/videos` (git-ignored). Don't use a folder called `showcase`, which Windows treats as the `Showcase` source folder.
   - `audioCapturePath`: the absolute path of `tools/ProcessAudioCapture/bin/Release/net10.0-windows/win-x64/ProcessAudioCapture.exe`.
   - `filter` to record only matching clips, and `mod` when more than one running mod has clips.
4. Run `tools/BridgeTools/Showcase/make_showcase.ps1`. It cuts one captioned MP4 per clip, plus `highlight_reel.mp4` when there's more than one, then deletes the recording unless given `-KeepRecording`.

For SillyAnimalThings, all 15 clips take about 95 s to record and 45 s to cut.

## How it works, and what not to break

- **One continuous recording.** The tool starts ffmpeg and the audio recorder, then logs each clip's start and end in `timeline.tsv`. Video frames carry the real time, so the script cuts both tracks at the same moment.
- **Video:** ffmpeg's `gfxcapture` records the game window, cropped to the middle 1280x720. It only gets frames when the window changes, so ffmpeg can't stop while the game isn't drawing. Never block the main thread waiting for it: that once added a 15 s delay.
- **Sound:** `ProcessAudioCapture` uses Windows' process loopback API, which records only the game, not the microphone or other programs. **Never record the microphone**, which is ffmpeg's only audio input on this machine. The recorder is self-contained, because the game can't find a per-user .NET install.
- **Don't film while stepping ticks.** Pawns' drawn positions don't update while the game is paused between steps, so they appear to turn and flash without moving. Clips run at normal speed.
- The game UI is hidden through its screenshot mode. Captions come from each ability's label and as much of its description as fits on one line (130 characters), so they follow the mod's own text.
- Stage within 12 cells across and 5 down from the origin: the frame ends there and the caption covers the bottom.
- Stage so the effect is obvious: choose targets that react visibly (for example, smaller than the caster for knockback), and keep fires and other hazards where the effect will clearly reach them.
