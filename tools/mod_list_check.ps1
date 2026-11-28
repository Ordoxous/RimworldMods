<#
.SYNOPSIS
Checks the player's active mod list: missing dependencies, load order against every mod's rules, and libraries or
frameworks that no active mod declares a need for.

.DESCRIPTION
Reads ModsConfig.xml (or -Config, such as a GABS profile's) and each active mod's About.xml from the Workshop, the game's
Mods folder and its DLC. Rules: loadAfter, loadBefore, forceLoadAfter, forceLoadBefore and modDependencies (also by
version). A "library nobody needs" is only a lead: mods can use one without declaring it, or patch it in only when it's
active (LoadFolders IfModActive), so check before removing.

.EXAMPLE
pwsh tools/mod_list_check.ps1
pwsh tools/mod_list_check.ps1 -Config ~/.gabs/rimworld/tweaks-multifloors/Config/ModsConfig.xml
#>
param(
    [string]$Config = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\ModsConfig.xml'),
    [string]$SteamLibrary = 'D:\SteamLibrary'
)
$ErrorActionPreference = 'Stop'
$active = @(([xml](Get-Content $Config -Raw)).ModsConfigData.activeMods.li | ForEach-Object { $_.ToLowerInvariant() })
$activeSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$active)
$position = @{}
for ($i = 0; $i -lt $active.Count; $i++) { $position[$active[$i]] = $i }

$roots = "$SteamLibrary\steamapps\workshop\content\294100", "$SteamLibrary\steamapps\common\RimWorld\Mods", "$SteamLibrary\steamapps\common\RimWorld\Data"
$mods = @{}
foreach ($root in $roots) {
    foreach ($dir in Get-ChildItem $root -Directory -ErrorAction SilentlyContinue) {
        $file = Join-Path $dir.FullName 'About\About.xml'
        if (-not (Test-Path $file)) { continue }
        try { $meta = ([xml](Get-Content $file -Raw)).ModMetaData } catch { continue }
        $id = "$($meta.packageId)".ToLowerInvariant()
        if ($id -and $activeSet.Contains($id) -and -not $mods.ContainsKey($id)) { $mods[$id] = $meta }
    }
}

function Ids($node) {
    if (-not $node) { return @() }
    @($node.li | ForEach-Object { if ($_ -is [string]) { $_ } elseif ($_.packageId) { $_.packageId } else { $_.'#text' } } | Where-Object { $_ } | ForEach-Object { "$_".ToLowerInvariant() })
}

$before = [System.Collections.Generic.List[object]]::new()   # a must load before b
$needs = @{}
foreach ($id in $mods.Keys) {
    $meta = $mods[$id]
    $deps = @(Ids $meta.modDependencies) + @(Ids $meta.modDependenciesByVersion.'v1.7')
    $needs[$id] = $deps
    foreach ($other in $deps + (Ids $meta.loadAfter) + (Ids $meta.forceLoadAfter)) { $before.Add(@($other, $id)) }
    foreach ($other in (Ids $meta.loadBefore) + (Ids $meta.forceLoadBefore)) { $before.Add(@($id, $other)) }
}
function Name($id) { if ($mods[$id]) { "$($mods[$id].name) [$id]" } else { $id } }

"Active mods: $($active.Count); About.xml found for $($mods.Count)"
$missingAbout = $active | Where-Object { -not $mods.ContainsKey($_) }
if ($missingAbout) { "Not found on disk: $($missingAbout -join ', ')" }

''
'== Missing dependencies'
$missing = foreach ($id in $active) { foreach ($dep in $needs[$id]) { if (-not $activeSet.Contains($dep) -and $dep -notmatch '^ludeon\.') { "  $(Name $id) needs $dep" } } }
if ($missing) { $missing } else { '  none' }

''
'== Load order against the rules'
$violations = foreach ($edge in $before) {
    $a, $b = $edge
    if ($position.ContainsKey($a) -and $position.ContainsKey($b) -and $position[$a] -gt $position[$b]) {
        "  $(Name $a) (#$($position[$a] + 1)) should load before $(Name $b) (#$($position[$b] + 1))"
    }
}
if ($violations) { $violations | Select-Object -Unique } else { '  none' }

''
'== Libraries and frameworks no active mod declares a need for (leads, not verdicts)'
$dependents = @{}
foreach ($id in $active) { foreach ($dep in $needs[$id]) { $dependents[$dep] = 1 + [int]$dependents[$dep] } }
$leads = foreach ($id in $active) {
    $name = "$($mods[$id].name)"
    if ($id -match '^ludeon\.' -or $dependents[$id] -or $name -notmatch 'Framework|\bLib\b|Library|\bCore\b|\bAPI\b|Extensions|Utilit|Prepatcher|Loader') { continue }
    "  $(Name $id)"
}
if ($leads) { $leads } else { '  none' }
