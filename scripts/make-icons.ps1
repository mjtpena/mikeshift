<#
.SYNOPSIS
  Regenerates every MikeShift icon from assets/brand/mikeshift.svg.

.DESCRIPTION
  Runs tools/IconGen (a build-time tool that is not part of the app or the package). It writes, under src/MikeShift.App/Assets:
    - MSIX logos: Square44x44Logo (scale-100/125/150/200/400 and targetsize-16/24/32/48/256, plus _altform-unplated and
      _altform-lightunplated), Square150x150Logo, Wide310x150Logo, StoreLogo, SplashScreen (scale-100/125/150/200/400)
    - MikeShift.ico            colour app icon (exe icon, window icons), 16..256 px
    - tray-dark-taskbar.ico    monochrome white glyph, 16/20/24/32/40/48/64 px (for a dark taskbar)
    - tray-light-taskbar.ico   monochrome near-black glyph, same sizes (for a light taskbar)
  and the legibility preview docs/images/icon-sheet.png.
  The tray picks the .ico that matches the taskbar theme at runtime (src/MikeShift.App/Tray/TrayController.cs).
  Edit the SVG, run this script, look at the sheet, commit the result.

.EXAMPLE
  ./scripts/make-icons.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet run --project tools/IconGen -c Release -- --repo $root
    if ($LASTEXITCODE -ne 0) { throw "IconGen failed with exit code $LASTEXITCODE" }
}
finally {
    Pop-Location
}
