<#
.SYNOPSIS
Reads things and designations out of a RimWorld save (.rws) without loading it, map by map.

.DESCRIPTION
For when the game isn't running, or to see a save from before something went wrong. Streams the XML, so a 50 MB save
takes a few seconds. Maps are named by their uniqueID, as the game's Map-16 / Map_17; MultiFloors levels are maps too.

.EXAMPLE
pwsh tools/save_query.ps1 Thilior '^Steel$'                 # things whose def or id matches, per map
pwsh tools/save_query.ps1 Thilior -Designations             # designations counted per map and def
pwsh tools/save_query.ps1 Thilior -Designations -Def MF_HaulToOtherLevelDesignation
#>
param(
    [Parameter(Mandatory)][string]$Save,
    [string]$Match = '.',
    [switch]$Designations,
    [string]$Def
)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path $Save)) {
    $Save = Join-Path $env:USERPROFILE "AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Saves\$Save.rws"
}

$settings = [System.Xml.XmlReaderSettings]::new()
$settings.IgnoreWhitespace = $true
$reader = [System.Xml.XmlReader]::Create($Save, $settings)
$regex = [regex]::new($Match, 'IgnoreCase')
$map = $null
$rows = [System.Collections.Generic.List[object]]::new()
try {
    $more = $reader.Read()
    while ($more -and -not $reader.EOF) {
        # savegame/game/maps/li/uniqueID: each map's id comes before its things and designations
        if ($reader.NodeType -eq 'Element' -and $reader.Depth -eq 4 -and $reader.Name -eq 'uniqueID') {
            $map = $reader.ReadElementContentAsString()
            continue
        }
        # savegame/game/maps/li/things/thing
        if (-not $Designations -and $reader.NodeType -eq 'Element' -and $reader.Depth -eq 5 -and $reader.Name -eq 'thing') {
            $thing = ([xml]$reader.ReadOuterXml()).thing
            if ($regex.IsMatch($thing.def) -or $regex.IsMatch($thing.id)) {
                $rows.Add([pscustomobject]@{ map = $map; def = $thing.def; id = $thing.id; pos = $thing.pos; stack = $thing.stackCount; stuff = $thing.stuff })
            }
            continue
        }
        # savegame/game/maps/li/designationManager/allDesignations/li
        if ($Designations -and $reader.NodeType -eq 'Element' -and $reader.Depth -eq 6 -and $reader.Name -eq 'li') {
            $designation = ([xml]$reader.ReadOuterXml()).li
            if ($designation.def -and $null -ne $designation.target -and (-not $Def -or $designation.def -eq $Def)) {
                $rows.Add([pscustomobject]@{ map = $map; def = $designation.def; target = $designation.target; destMap = $designation.mf_destMap; requester = $designation.ordo_requester })
            }
            continue
        }
        $more = $reader.Read()
    }
}
finally {
    $reader.Close()
}

if ($Designations -and -not $Def) {
    $rows | Group-Object map, def | Sort-Object Count -Descending | Select-Object Count, Name | Format-Table -AutoSize
}
else {
    $rows | Format-Table -AutoSize | Out-String -Width 250
}
