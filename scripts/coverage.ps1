<#
.SYNOPSIS
  Runs the whole test suite under coverlet and writes a merged coverage report to artifacts/coverage/.

.DESCRIPTION
  dotnet test <each test project> --collect:"XPlat Code Coverage" --settings tests/coverage.runsettings
  dotnet reportgenerator (local tool, see .config/dotnet-tools.json) merges the per-project Cobertura files into
    artifacts/coverage/index.html     HTML report
    artifacts/coverage/Summary.txt    text summary (overall, per assembly, per class)
    artifacts/coverage/Cobertura.xml  merged Cobertura
    artifacts/coverage/Namespaces.txt line/branch % per namespace + the least-covered classes (also printed)

  Measured: Shiftvert.Core and Shiftvert.Cli. Not measured: Shiftvert.App (WinUI), generated code (CsWin32 Windows.Win32.*,
  *.g.cs, [GeneratedCode], [ExcludeFromCodeCoverage]).
  Note: the CLI child-process tests launch shiftvert.exe out of process, which coverlet cannot see; the in-process CliRunner
  tests (Shiftvert.Core.Tests) count.

.PARAMETER Configuration  Debug (default) or Release.
.PARAMETER Filter         Optional `dotnet test --filter` expression (e.g. "Category!=Slow").
.PARAMETER NoBuild        Skip the build (assumes the solution was already built with the same configuration).
.PARAMETER Worst          How many of the least-covered classes to list (default 15).
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Debug',
    [string] $Filter,
    [switch] $NoBuild,
    [int] $Worst = 15
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $out = Join-Path $root 'artifacts/coverage'
    $raw = Join-Path $out 'raw'
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Path $raw | Out-Null

    # One project at a time: the CLI tests launch shiftvert.exe against 30 s limits, and running them next to the 1000-test Core
    # suite under instrumentation starves them (Word-backed cases time out).
    $testExit = 0
    foreach ($proj in 'tests/Shiftvert.Core.Tests', 'tests/Shiftvert.Cli.Tests') {
        $testArgs = @('test', $proj, '-c', $Configuration, '--nologo', '--collect:XPlat Code Coverage',
                      '--settings', 'tests/coverage.runsettings', '--results-directory', $raw)
        if ($NoBuild) { $testArgs += '--no-build' }
        if ($Filter) { $testArgs += @('--filter', $Filter) }
        # Coverlet occasionally fails to write its report when the test host is slow to exit (it cannot restore the instrumented
        # assembly while the host still has it loaded): then no new Cobertura file appears, so try that project once more.
        for ($attempt = 1; $attempt -le 2; $attempt++) {
            $before = @(Get-ChildItem $raw -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue).Count
            Write-Host "==> dotnet $($testArgs -join ' ')" -ForegroundColor Cyan
            & dotnet @testArgs
            $code = $LASTEXITCODE
            $after = @(Get-ChildItem $raw -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue).Count
            if ($after -gt $before) { break }
            Write-Warning "$proj produced no coverage report (attempt $attempt)."
        }
        if ($code -ne 0) { $testExit = $code }
    }

    $reports = @(Get-ChildItem $raw -Recurse -Filter coverage.cobertura.xml)
    if ($reports.Count -eq 0) { throw 'No coverage.cobertura.xml was produced.' }

    Write-Host '==> reportgenerator' -ForegroundColor Cyan
    dotnet tool restore | Out-Null
    $list = ($reports.FullName -join ';')
    dotnet reportgenerator "-reports:$list" "-targetdir:$out" '-reporttypes:Html;TextSummary;Cobertura' `
        '-assemblyfilters:+Shiftvert.Core;+Shiftvert.Cli' '-verbosity:Warning'
    if ($LASTEXITCODE -ne 0) { throw 'reportgenerator failed' }
    if (Test-Path (Join-Path $out 'Summary.txt')) { } elseif (Test-Path (Join-Path $out 'TextSummary.txt')) { Rename-Item (Join-Path $out 'TextSummary.txt') 'Summary.txt' }

    # Per-namespace and worst-class numbers from the merged Cobertura.
    [xml] $xml = Get-Content (Join-Path $out 'Cobertura.xml') -Raw
    $rows = foreach ($c in $xml.SelectNodes('//class')) {
        $full = ($c.name -replace '/.*$', '' -replace '\+.*$', '' -replace '<.*$', '')
        $lines = @($c.SelectNodes('lines/line'))
        $hit = @($lines | Where-Object { [int]$_.hits -gt 0 }).Count
        $bv = 0; $bh = 0
        foreach ($l in $lines) {
            if ($l.'condition-coverage' -match '\((\d+)/(\d+)\)') { $bh += [int]$Matches[1]; $bv += [int]$Matches[2] }
        }
        [pscustomobject]@{ Class = $full; Namespace = ($full -replace '\.[^.]+$', ''); Lines = $lines.Count; LinesHit = $hit; Branches = $bv; BranchesHit = $bh }
    }
    # Nested/compiler-generated types fold into their outer class.
    $merged = $rows | Group-Object Class | ForEach-Object {
        [pscustomobject]@{
            Class = $_.Name; Namespace = $_.Group[0].Namespace
            Lines = ($_.Group | Measure-Object Lines -Sum).Sum; LinesHit = ($_.Group | Measure-Object LinesHit -Sum).Sum
            Branches = ($_.Group | Measure-Object Branches -Sum).Sum; BranchesHit = ($_.Group | Measure-Object BranchesHit -Sum).Sum
        }
    }
    function Pct($hit, $total) { if ($total -eq 0) { 'n/a' } else { '{0:N1}' -f (100.0 * $hit / $total) } }
    function Sum($g, $p) { ($g | Measure-Object $p -Sum).Sum }

    $sb = [System.Text.StringBuilder]::new()
    [void] $sb.AppendLine('Coverage by namespace')
    [void] $sb.AppendLine(('{0,-46} {1,7} {2,9} {3,9}' -f 'Namespace', 'Lines', 'Line %', 'Branch %'))
    $merged | Group-Object Namespace | Sort-Object Name | ForEach-Object {
        [void] $sb.AppendLine(('{0,-46} {1,7} {2,9} {3,9}' -f $_.Name, (Sum $_.Group Lines), (Pct (Sum $_.Group LinesHit) (Sum $_.Group Lines)), (Pct (Sum $_.Group BranchesHit) (Sum $_.Group Branches))))
    }
    foreach ($asm in 'Shiftvert.Core', 'Shiftvert.Cli') {
        $g = @($merged | Where-Object { $_.Namespace -eq $asm -or $_.Namespace -like "$asm.*" })
        if ($g.Count -gt 0) {
            [void] $sb.AppendLine(('{0,-46} {1,7} {2,9} {3,9}' -f "TOTAL $asm", (Sum $g Lines), (Pct (Sum $g LinesHit) (Sum $g Lines)), (Pct (Sum $g BranchesHit) (Sum $g Branches))))
        }
    }
    [void] $sb.AppendLine()
    [void] $sb.AppendLine("Least-covered classes (at least 10 lines), worst $Worst")
    $merged | Where-Object { $_.Lines -ge 10 } | Sort-Object { $_.LinesHit / $_.Lines } | Select-Object -First $Worst | ForEach-Object {
        [void] $sb.AppendLine(('{0,-70} {1,5} lines {2,6}%' -f $_.Class, $_.Lines, (Pct $_.LinesHit $_.Lines)))
    }
    $sb.ToString() | Set-Content (Join-Path $out 'Namespaces.txt')
    Write-Host ''
    Write-Host (Get-Content (Join-Path $out 'Summary.txt') -Raw)
    Write-Host $sb.ToString()
    Write-Host "HTML report: $(Join-Path $out 'index.html')" -ForegroundColor Green
    if ($testExit -ne 0) { throw "dotnet test failed (exit code $testExit); the report above covers the tests that ran." }
}
finally {
    Pop-Location
}
