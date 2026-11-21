# Builds a mod and copies it into the RimWorld Mods folder, stopping at the first failure.
# Usage: Deploy.ps1 <ProjectName>, or Deploy.ps1 All for every project in the repo.
param(
    [Parameter(Mandatory = $true)]
    [string]$Project
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Use the first dotnet that has an SDK: Rider's PATH can put a runtime-only install in Program Files ahead of ~/.dotnet
$candidates = @(Join-Path $env:USERPROFILE '.dotnet\dotnet.exe') +
    @(Get-Command dotnet -All -ErrorAction SilentlyContinue | ForEach-Object { $_.Source })
$dotnet = $candidates | Where-Object { (Test-Path $_) -and (& $_ --list-sdks 2>$null) } | Select-Object -First 1
if (-not $dotnet) {
    throw "No dotnet with an SDK found. Tried: $($candidates -join ', ')"
}
Write-Host "Using $dotnet on PowerShell $($PSVersionTable.PSVersion)"

if ($Project -eq 'All') {
    $projects = Get-ChildItem -Path $root -Filter '*.csproj' -Recurse -Depth 1 | ForEach-Object { $_.BaseName }
} else {
    $projects = @($Project)
}

foreach ($name in $projects) {
    $csproj = Join-Path $root "$name\$name.csproj"
    if (-not (Test-Path $csproj)) {
        throw "No project at $csproj"
    }

    Write-Host "=== Deploying $name"
    # CopyToRimworld builds first; Debug is the default, but say so since hot reload and the debugger need it
    & $dotnet build $csproj -c Debug -t:CopyToRimworld --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "=== Deploy failed: $name"
        exit $LASTEXITCODE
    }
}

Write-Host "=== Deployed: $($projects -join ', ')"
