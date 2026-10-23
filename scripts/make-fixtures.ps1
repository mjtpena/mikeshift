<#
.SYNOPSIS
  Generates the manual-testing fixture set (tiny image/PDF/media/document samples) into a folder.

.DESCRIPTION
  The generators live in the test project (tests/MikeShift.Core.Tests/Infrastructure/Fixtures*.cs) so the automated
  tests and this script share one source of truth. This script just runs the `FixtureExport` test with
  MIKESHIFT_FIXTURES_OUT pointing at the output folder. Every converter package contributes its files by implementing
  its `Write<Area>Fixtures(string dir)` hook in its own Fixtures.<Area>.cs.

  Then try the files with the CLI, e.g.:
      dotnet run --project src/MikeShift.Cli -- --list .\fixtures\sample.png
      dotnet run --project src/MikeShift.Cli -- --run Zip .\fixtures\folder

.PARAMETER OutDir  Output folder (created if missing). Default: ./fixtures (git-ignored).
#>
[CmdletBinding()]
param(
    [string] $OutDir = (Join-Path (Split-Path -Parent $PSScriptRoot) 'fixtures')
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

Push-Location $root
try {
    $env:MIKESHIFT_FIXTURES_OUT = $OutDir
    dotnet test tests/MikeShift.Core.Tests --nologo --filter "FullyQualifiedName~FixtureExport" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Fixture generation failed (exit code $LASTEXITCODE)" }
}
finally {
    Remove-Item Env:\MIKESHIFT_FIXTURES_OUT -ErrorAction SilentlyContinue
    Pop-Location
}

Write-Host "Fixtures written to $OutDir" -ForegroundColor Green
Get-ChildItem $OutDir -Recurse -File | Select-Object -ExpandProperty FullName | ForEach-Object { Write-Host "  $_" }
