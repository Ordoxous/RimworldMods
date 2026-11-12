# Applies C# changes to a running game without a restart, through .NET hot reload.
#
#   hot_reload.ps1 -Project <ModFolder> [-Reset]
#
# -Project is the mod's folder in the repo, which is also its csproj, assembly and deployed folder name.
#
#   1. After deploying a build the normal way (and starting the game), run with -Reset. That saves the compiler arguments and
#      snapshots the source the deployed DLL was built from.
#   2. After each edit, run without -Reset. It writes a delta and prints its folder; pass that to the ordo/apply_update
#      bridge tool as directory, with the mod's name as mod when more than one Ordo mod with code is loaded.
#
# Deltas chain, so each run snapshots the source it applied. Restarting the game loads the deployed DLL again, without the
# deltas: deploy a fresh build and -Reset. Defs (XML) changes go through ordo/hot_reload_defs instead.
param(
	[switch]$Reset,
	[Parameter(Mandatory)]
	[string]$Project,
	[string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$projectDir = Join-Path $root $Project
$state = Join-Path $PSScriptRoot "state\$Project"
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet)
{
	$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
}

$steamLibrary = 'D:\SteamLibrary'
$localProps = Join-Path $root 'Directory.Build.local.props'
if (Test-Path $localProps)
{
	$match = Select-String -Path $localProps -Pattern '<SteamLibraryDirectory>(.*)</SteamLibraryDirectory>'
	if ($match)
	{
		$steamLibrary = $match.Matches[0].Groups[1].Value
	}
}

$loadedDll = Join-Path $steamLibrary "steamapps\common\RimWorld\Mods\$Project\Assemblies\$Project.dll"
$tool = Join-Path $PSScriptRoot 'bin\Debug\net10.0\HotReloadDelta.dll'
if (-not (Test-Path $tool))
{
	& $dotnet build (Join-Path $PSScriptRoot 'HotReloadDelta.csproj') -nologo -v q | Out-Null
}

function Snapshot([string]$name)
{
	$target = Join-Path $state $name
	New-Item -ItemType Directory -Force $target | Out-Null
	Copy-Item (Join-Path $projectDir 'src') $target -Recurse -Force
	$target
}

if ($Reset)
{
	if (Test-Path $state)
	{
		Remove-Item $state -Recurse -Force
	}

	New-Item -ItemType Directory -Force $state | Out-Null

	# The exact csc command line, so the deltas compile with the same references and options as the deployed DLL
	$log = & $dotnet build (Join-Path $projectDir "$Project.csproj") -c $Configuration --no-incremental -nologo -v n 2>&1
	$csc = $log | Where-Object { $_ -match 'csc\.exe ' } | Select-Object -First 1
	if (-not $csc)
	{
		throw "Couldn't find the compiler command line in the build output"
	}

	($csc -replace '^.*?csc\.exe ', '') | Set-Content (Join-Path $state 'csc.args') -Encoding utf8

	$built = Join-Path $projectDir "out\Assemblies\$Project.dll"
	if ((Get-FileHash $built).Hash -ne (Get-FileHash $loadedDll).Hash)
	{
		Write-Warning "The deployed DLL isn't this build. Deploy it (dotnet build -c $Configuration -t:CopyToRimworld) and restart the game first."
	}

	Snapshot '0' | Out-Null
	"Hot reload baseline saved for $Project"
	exit 0
}

if (-not (Test-Path (Join-Path $state 'csc.args')))
{
	throw "No baseline yet: run with -Reset after deploying"
}

$snapshots = Get-ChildItem $state -Directory | Where-Object { $_.Name -match '^\d+$' } | Sort-Object { [int]$_.Name }
$next = [int]$snapshots[-1].Name + 1
$delta = Join-Path $state "delta-$next"

& $dotnet $tool (Join-Path $state 'csc.args') $projectDir $loadedDll $delta @($snapshots.FullName)
$exit = $LASTEXITCODE
if ($exit -eq 4)
{
	exit 0
}

if ($exit -ne 0)
{
	if (Test-Path $delta)
	{
		Remove-Item $delta -Recurse -Force
	}

	Write-Warning 'No delta: restart the game to load this change'
	exit $exit
}

Snapshot "$next" | Out-Null
"Delta $next ready. Apply it with ordo/apply_update, directory: $delta"
