# Builds a mod and copies it into the RimWorld Mods folder, stopping at the first failure.
# Usage: Deploy.ps1 <ProjectName>, or Deploy.ps1 All for every project in the repo.
param(
    [Parameter(Mandatory = $true)]
    [string]$Project
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet) {
    $dotnet = $dotnet.Source
} else {
    $dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
    if (-not (Test-Path $dotnet)) {
        throw 'Could not find dotnet on PATH or in ~/.dotnet'
    }
}

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
    # Without an explicit configuration the build goes to bin\ rather than out\, and CopyToRimworld would deploy a stale out\
    & $dotnet build $csproj -c Debug '-t:Build;CopyToRimworld' --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "=== Deploy failed: $name"
        exit $LASTEXITCODE
    }
}

Write-Host "=== Deployed: $($projects -join ', ')"
