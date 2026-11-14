<#
.SYNOPSIS
Gives focus back to whatever had it when a windowed RimWorld takes it on launch.

.DESCRIPTION
Waylith creates its window through GLFW with focus-on-show on and has no option to turn it off, so a windowed game takes
the keyboard when its window appears. Start this in the background just before games_start: it notes the window that has
focus, waits for a RimWorld window to take it, and gives it back. It keeps watching for -GraceSeconds afterwards, because
the game can take focus again as it finishes loading, so clicking into the game in that time is undone too. It exits after
-TimeoutSeconds if the game never takes focus. A minimized game stops drawing, so this restores focus rather than
minimizing the game.
#>
param(
	[int]$TimeoutSeconds = 240,
	[int]$GraceSeconds = 30
)

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class FocusNative
{
	[DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
	[DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
	[DllImport("user32.dll")] public static extern bool IsWindow(IntPtr window);
	[DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
	[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
	[DllImport("user32.dll")] public static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
"@

function Describe([IntPtr]$window) {
	$id = [uint32]0
	[void][FocusNative]::GetWindowThreadProcessId($window, [ref]$id)
	$name = (Get-Process -Id $id -ErrorAction SilentlyContinue).ProcessName
	$title = New-Object System.Text.StringBuilder 256
	[void][FocusNative]::GetWindowText($window, $title, 256)
	return @{ Process = $name; Title = $title.ToString() }
}

function Restore([IntPtr]$window) {
	# Windows only lets a background process set the foreground window just after a key event, so tap Alt first
	[FocusNative]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
	[FocusNative]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
	return [FocusNative]::SetForegroundWindow($window)
}

$previous = [FocusNative]::GetForegroundWindow()
$owner = Describe $previous
if ($owner.Process -eq "RimWorld") {
	Write-Output "RimWorld already has focus; nothing to give back"
	exit 0
}

Write-Output "Watching for RimWorld to take focus from $($owner.Process): $($owner.Title)"
$clock = [System.Diagnostics.Stopwatch]::StartNew()
$firstTaken = $null
$restores = 0
while ($clock.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
	if ($firstTaken -ne $null -and ($clock.Elapsed.TotalSeconds - $firstTaken) -gt $GraceSeconds) {
		break
	}

	if (-not [FocusNative]::IsWindow($previous)) {
		Write-Output "The window that had focus has closed"
		exit 0
	}

	$current = [FocusNative]::GetForegroundWindow()
	if ($current -ne $previous -and (Describe $current).Process -eq "RimWorld") {
		if ($firstTaken -eq $null) {
			$firstTaken = $clock.Elapsed.TotalSeconds
		}

		$given = Restore $previous
		$restores++
		Write-Output ("{0:N1}s: RimWorld took focus; given back: {1}" -f $clock.Elapsed.TotalSeconds, $given)
	}

	Start-Sleep -Milliseconds 100
}

if ($restores -eq 0) {
	Write-Output "RimWorld never took focus within $TimeoutSeconds s"
}
else {
	Write-Output "Gave focus back $restores time(s)"
}
