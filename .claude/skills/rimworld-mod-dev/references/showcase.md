# Recording a showcase

`tools/BridgeTools/Showcase` records a staged clip of each ability at normal speed with the game's sound, cut into captioned 60 fps MP4s and a highlight reel. It's separate from the tests: the clips' checks only decide whether a take is worth keeping, and a clip gets up to 3 takes. The two share only `Stage.cs`.

## Recording

1. Install ffmpeg (`winget install Gyan.FFmpeg`). Build the audio recorder once: `dotnet build tools/ProcessAudioCapture/ProcessAudioCapture.csproj -c Release`.
2. Start the mod's showcase profile (e.g. `sillyanimalthings-showcase`): a 1080p window, no music, and `runInBackground` on so that it keeps playing sound without focus. Don't minimize the window. The game's sound plays out loud while recording.
3. Call `ordo/record_showcase` with:
   - `outputDirectory`: the absolute path of `tools/BridgeTools/videos` (git-ignored). Don't use a folder called `showcase`, which Windows treats as the `Showcase` source folder.
   - `audioCapturePath`: the absolute path of `tools/ProcessAudioCapture/bin/Release/net10.0-windows/win-x64/ProcessAudioCapture.exe`.
   - `filter` to record only matching clips, `append` to keep the folder's other clips (see below), and `mod` when more than one running mod has clips.
4. Run `tools/BridgeTools/Showcase/make_showcase.ps1`. Pass `-OutputDirectory` if you recorded somewhere other than `videos`, and `-ModName` for the reel's title card (it defaults to Silly Animal Things). It cuts one captioned MP4 per clip, plus `highlight_reel.mp4` when there's more than one, then deletes the recording unless given `-KeepRecording`.

Give each mod its own folder under `videos`. For SillyAnimalThings (`videos/sillyanimalthings`), all 22 clips take about 2.5 min to record and a minute to cut; re-recorded on 2026-11-14 after the switch to whole-window recording, all 22 recorded (charge on its second take) and a frame from each checked. MechUpgradesUi's 4 clips take about a minute to record (`videos/mechupgradesui`), and BlueprintMaterials' 2 about 30 s (`videos/blueprintmaterials`).

**Keep the showcase window at 1920x1080.** The game saves its window size and position to the profile's `Prefs.xml` when the window is resized, so resizing it by hand changes every later recording. On 2026-11-14 a showcase window that had been resized to 1920x1009 on the second monitor made ffmpeg fail with "height not divisible by 2", recording no video while the clips still played; the tool reported "ffmpeg stopped early with exit code -542398533". With the game stopped, set `screenWidth` 1920, `screenHeight` 1080, `windowPositionX` 0 and `windowPositionY` 38 again. The second monitor is too short for a 1080p window once its taskbar is counted, so the showcase window goes on the main one. To check the window's real size, capture 2 s with the same `gfxcapture` filter and read it with ffprobe: the main monitor is scaled, so Win32 sizes read from a script that isn't DPI-aware are wrong.

## Clips that aren't abilities

`ShowcaseClip.Feature(name, title, caption, run, showUi, frame)` makes a clip with its own title and caption, for dialogs, tabs and buildings at work. MechUpgradesUi's clips are the example.

- **`showUi: true`** keeps the game's UI on screen for that clip. The learning helper is turned off and old messages are cleared while it records.
- **`frame`:**
  - `ShowcaseFrame.Middle` (the default) shows the middle 1280x720 of the window at full size. Use it for the map around the stage, or for a dialog, which opens centred.
  - `ShowcaseFrame.Window` shows the whole window scaled down to 1280x720, with the caption at the top instead of the bottom. Use it for anything at the window's edges: main tabs, the inspect pane and gizmos.
- **`ShowcaseStage.Hold(seconds)`** waits in real time. A dialog with `forcePause` pauses the game, so `Step`, `Beat` and `Linger` would never finish while one is open.
- **Clean up what the clip opened:** close its dialogs and main tabs, clear the selection, and remove anything it added to the world (such as policies) in a `finally`. `Stage.CleanUp` only removes what was spawned.
- **Tooltips:** `stage.Hover(position)` moves RimBridge's virtual cursor to a point in UI coordinates (top-left origin), so tooltips, mouseover highlights and float menus opening at the cursor all behave as for a player. The real mouse doesn't disturb it, and it's cleared after every clip. RimBridge keeps the pointer internal (`RimBridgeVirtualPointer`), so it's reached by reflection, and the clip fails with a message if a RimBridge update renames it. Finding where to hover is up to the clip: MechUpgradesUi's `policy_tooltips` works out Mechs tab cells from `PawnTable`'s private layout, and float menu options from their `RequiredHeight`. Give a tooltip about 4 s: it appears after a short delay, and viewers need time to read it.
- **Gizmos:** `stage.GizmoCenter(gizmo => ...)` gives the middle of a gizmo's button in the grid for the current selection, for `Hover`. It follows the grid as measured on 1.7: one row starting 14 px right of the inspect pane, 75 px buttons 5 px apart, in the order of `Stage.SelectionGizmos()` (reverse designators such as Cancel included, sorted by `Order`). It fails if the gizmo would be on a second row. A float menu opened with the cursor there appears above the gizmo, as for a player. BlueprintMaterials' clips pause the game while the cursor works the menu, as a player would, so a colonist on the way doesn't arrive first.

## How it works, and what not to break

- **One continuous recording.** The tool starts ffmpeg and the audio recorder, then logs each clip's start and end in `timeline.tsv`. Video frames carry the real time, so the script cuts both tracks at the same moment.
- **Video:** ffmpeg's `gfxcapture` records the whole game window. `timeline.tsv` gives each clip's frame, and `make_showcase.ps1` crops it to the middle 1280x720 or scales it down to that size. It only gets frames when the window changes, so ffmpeg can't stop while the game isn't drawing. Never block the main thread waiting for it: that once added a 15 s delay.
- **Sound:** `ProcessAudioCapture` uses Windows' process loopback API, which records only the game, not the microphone or other programs. **Never record the microphone**, which is ffmpeg's only audio input on this machine. The recorder is self-contained, because the game can't find a per-user .NET install.
- **Don't film while stepping ticks.** Pawns' drawn positions don't update while the game is paused between steps, so they appear to turn and flash without moving. Clips run at normal speed.
- The game UI is hidden through its screenshot mode, unless a clip keeps it. An ability clip's caption comes from each ability's label and as much of its description as fits on one line (130 characters), so they follow the mod's own text. A clip about when an animal uses an ability, rather than what it does, passes its own `caption` to `ShowcaseClip` (SillyAnimalThings' `ai_*` clips).
- **Re-recording some clips:** pass `append: true` with a `filter`. The folder's other clip videos stay, and `make_showcase.ps1` replaces only the clips recorded again and rebuilds the reel from every numbered clip in the folder. A clip that fails to record keeps its old video. Append is refused while the folder holds a recording that hasn't been cut yet, since a new one would overwrite it. Without `append`, recording replaces all of the folder's MP4s.
- Use a subfolder (such as `videos/ai_behaviours`) for a reel of just some clips.
- Stage within 12 cells across and 5 down from the origin: the frame ends there and the caption covers the bottom.
- Stage so the effect is obvious: choose targets that react visibly (for example, smaller than the caster for knockback), and keep fires and other hazards where the effect will clearly reach them.
