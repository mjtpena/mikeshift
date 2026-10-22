<#
.SYNOPSIS
  Build (and optionally test / package) Shiftvert from the command line.

.DESCRIPTION
  EXACT COMMANDS (what this script runs; use them directly if you prefer):

    Everything, Debug (works from a plain shell; the solution maps the WinUI app to x64):
        dotnet build Shiftvert.sln
    Everything, Release:
        dotnet build Shiftvert.sln -c Release
    All tests (Core + CLI; the CLI tests launch the built shiftvert.exe):
        dotnet test Shiftvert.sln
    One test project / filter:
        dotnet test tests/Shiftvert.Core.Tests --filter "FullyQualifiedName~Naming"
    WinUI app for a specific architecture (the app needs an explicit platform if built on its own):
        dotnet build src/Shiftvert.App -p:Platform=x64
        dotnet build src/Shiftvert.App -p:Platform=ARM64
    Unsigned MSIX (CI uses this; install needs signing, see packaging/ later):
        dotnet build src/Shiftvert.App -c Release -p:Platform=x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never
        -> src/Shiftvert.App/AppPackages/Shiftvert.App_<ver>_x64_Test/*.msix

  Core, Cli and the test projects are platform-neutral and build with plain `dotnet build` / `dotnet test`.

.PARAMETER Configuration  Debug (default) or Release.
.PARAMETER Test           Also run `dotnet test Shiftvert.sln` after building.
.PARAMETER Msix           Also build the unsigned MSIX for each platform in -Platforms.
.PARAMETER Platforms      Platforms for -Msix. Default: x64.
.PARAMETER Clean          `dotnet clean` first.

.EXAMPLE
  ./scripts/build.ps1 -Test
.EXAMPLE
  ./scripts/build.ps1 -Configuration Release -Msix -Platforms x64,ARM64
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [switch] $Test,
    [switch] $Msix,
    [string[]] $Platforms = @('x64'),
    [switch] $Clean
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    function Invoke-Step([string] $title, [scriptblock] $block) {
        Write-Host "==> $title" -ForegroundColor Cyan
        & $block
        if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)" }
    }

    if ($Clean) { Invoke-Step 'dotnet clean' { dotnet clean Shiftvert.sln -c $Configuration --nologo -v quiet } }

    Invoke-Step "dotnet build Shiftvert.sln -c $Configuration" { dotnet build Shiftvert.sln -c $Configuration --nologo }

    if ($Test) {
        Invoke-Step "dotnet test Shiftvert.sln -c $Configuration" { dotnet test Shiftvert.sln -c $Configuration --no-build --nologo }
    }

    if ($Msix) {
        foreach ($p in $Platforms) {
            Invoke-Step "MSIX ($p)" {
                dotnet build src/Shiftvert.App -c $Configuration "-p:Platform=$p" `
                    -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never --nologo
            }
        }
        Get-ChildItem src/Shiftvert.App/AppPackages -Recurse -Include *.msix -ErrorAction SilentlyContinue |
            ForEach-Object { Write-Host "MSIX: $($_.FullName)" -ForegroundColor Green }
    }
}
finally {
    Pop-Location
}
