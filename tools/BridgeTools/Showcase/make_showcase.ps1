# Cuts what ordo/record_showcase recorded into a captioned video per clip and a highlight reel, then removes the recording.
# Needs ffmpeg: winget install Gyan.FFmpeg
param(
	[string]$OutputDirectory = (Join-Path $PSScriptRoot '..\videos'),
	[string]$ModName = 'Silly Animal Things',
	[switch]$KeepRecording
)

$ErrorActionPreference = 'Stop'

$framesPerSecond = 60
$fadeSeconds = 0.25
$titleCardSeconds = 2.5
$size = '1280x720'
$boldFont = 'C\:/Windows/Fonts/segoeuib.ttf'
$font = 'C\:/Windows/Fonts/segoeui.ttf'
$invariant = [Globalization.CultureInfo]::InvariantCulture

# The same settings for everything, so the clips can be joined without encoding them again
$encode = '-c:v', 'libx264', '-preset', 'slow', '-crf', '20', '-pix_fmt', 'yuv420p', '-r', "$framesPerSecond", '-c:a', 'aac', '-b:a', '160k', '-ar', '48000', '-ac', '2'

function Seconds([double]$value)
{
	$value.ToString('0.000', $invariant)
}

$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$video = Join-Path $OutputDirectory 'capture.mkv'
$audio = Join-Path $OutputDirectory 'capture.wav'
$timeline = Join-Path $OutputDirectory 'timeline.tsv'
if (-not (Test-Path $video) -or -not (Test-Path $audio) -or -not (Test-Path $timeline))
{
	throw "No recording in $OutputDirectory"
}

# Each frame was stamped with the time it was taken, so this is when the video starts
$videoStart = [double]::Parse((& ffprobe -v error -show_entries format=start_time -of csv=p=0 $video), $invariant)
$audioStart = [double]::Parse((Get-Content (Join-Path $OutputDirectory 'capture_audio_start.txt')), $invariant) / 1000

$failed = 0
$clipVideos = @()
foreach ($line in Get-Content $timeline)
{
	$name, $startMs, $endMs = $line -split "`t"
	$start = [double]::Parse($startMs, $invariant) / 1000
	$duration = [double]::Parse($endMs, $invariant) / 1000 - $start
	$fadeOutStart = Seconds ($duration - $fadeSeconds)

	$videoFilter = @(
		"drawbox=x=0:y=ih-92:w=iw:h=92:color=black@0.6:t=fill"
		"drawtext=fontfile='$boldFont':textfile=title.txt:expansion=none:fontsize=30:fontcolor=white:x=28:y=h-84"
		"drawtext=fontfile='$font':textfile=caption.txt:expansion=none:fontsize=20:fontcolor=white:x=28:y=h-42"
		"fade=t=in:d=$fadeSeconds"
		"fade=t=out:st=${fadeOutStart}:d=$fadeSeconds"
	) -join ','
	$audioFilter = "afade=t=in:d=$fadeSeconds,afade=t=out:st=${fadeOutStart}:d=$fadeSeconds"

	$output = Join-Path $OutputDirectory "$name.mp4"

	# drawtext finds its text files relative to here, which saves escaping their full paths
	Push-Location (Join-Path $OutputDirectory "clips\$name")
	try
	{
		# Seeks each input to the same moment, measured from when that input started
		& ffmpeg -hide_banner -loglevel error -y `
			-ss (Seconds ($start - $videoStart)) -t (Seconds $duration) -i $video `
			-ss (Seconds ($start - $audioStart)) -t (Seconds $duration) -i $audio `
			-map 0:v -map 1:a -vf $videoFilter -af $audioFilter @encode -movflags +faststart $output
	}
	finally
	{
		Pop-Location
	}

	if ($LASTEXITCODE -ne 0)
	{
		Write-Warning "Couldn't cut $name"
		$failed++
		continue
	}

	$clipVideos += $output
}

# Every clip in the folder, not just the ones cut now, so a recording made with append joins the earlier clips. The number
# in each name keeps them in the order the mod lists them.
$clipVideos = @(Get-ChildItem $OutputDirectory -Filter '*.mp4' | Where-Object Name -Match '^\d\d_.+\.mp4$' | Sort-Object Name | ForEach-Object FullName)
if ($clipVideos.Count -gt 1)
{
	$titleCard = Join-Path $OutputDirectory 'title_card.tmp.mp4'
	$titleFilter = "drawtext=fontfile='$boldFont':text='$ModName':fontsize=64:fontcolor=white:x=(w-text_w)/2:y=(h-text_h)/2,fade=t=in:d=0.4,fade=t=out:st=$(Seconds ($titleCardSeconds - 0.4)):d=0.4"
	& ffmpeg -hide_banner -loglevel error -y `
		-f lavfi -i "color=c=0x202428:s=${size}:r=${framesPerSecond}:d=$titleCardSeconds" `
		-f lavfi -t $titleCardSeconds -i 'anullsrc=r=48000:cl=stereo' `
		-vf $titleFilter @encode $titleCard
	if ($LASTEXITCODE -ne 0)
	{
		throw "Couldn't make the title card"
	}

	$list = Join-Path $OutputDirectory 'reel.tmp.txt'
	@($titleCard) + $clipVideos | ForEach-Object { "file '$($_ -replace '\\', '/')'" } | Set-Content $list -Encoding ascii
	& ffmpeg -hide_banner -loglevel error -y -f concat -safe 0 -i $list -c copy -movflags +faststart (Join-Path $OutputDirectory 'highlight_reel.mp4')
	$reelExit = $LASTEXITCODE
	Remove-Item $titleCard, $list
	if ($reelExit -ne 0)
	{
		throw "Couldn't join the clips into a reel"
	}
}

if ($failed -eq 0 -and -not $KeepRecording)
{
	Remove-Item $video, $audio, $timeline, (Join-Path $OutputDirectory 'capture_audio_start.txt')
	Remove-Item (Join-Path $OutputDirectory 'clips') -Recurse
}

$videos = Get-ChildItem $OutputDirectory -Filter *.mp4
$sizeMb = [math]::Round(($videos | Measure-Object Length -Sum).Sum / 1MB, 1)
"$($videos.Count) videos, $sizeMb MB, $failed failed"
exit $failed
