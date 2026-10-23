<#
.SYNOPSIS
  Builds the trimmed x64 + ARM64 MSIX packages, writes each as a standalone .msix AND bundles both into one .msixbundle, optionally signs
  everything, and writes SHA256SUMS.txt.

.DESCRIPTION
  Two flavours (-Flavor), both IL-trimmed (Trimming.props):
    sideload (default)  Windows App SDK bundled in the package. Nothing to install first: the safest file to hand to someone.
    store               Windows App SDK as the shared framework package Microsoft.WindowsAppRuntime.2 (about half the size). The Microsoft
                        Store installs that framework automatically. A sideloaded install needs it present first (see the README).
    all                 both of the above.

  Output (default <repo>\artifacts\), sideload names first, store names in brackets:
      MikeShift_<version>_x64.msix        [MikeShift_<version>_store_x64.msix]    one download per PC type: x64 for most PCs
      MikeShift_<version>_arm64.msix      [MikeShift_<version>_store_arm64.msix]  arm64 for Snapdragon / ARM PCs
      MikeShift_<version>.msixbundle      [MikeShift_<version>_store.msixbundle]  both architectures in one file (Windows picks the right one)
      SHA256SUMS.txt                      sha256sum format ("<hash>  <file>") for every package in the folder; verify with `sha256sum -c` or Get-FileHash

  Steps, per flavour:
    1. For each platform: a clean dotnet build src/MikeShift.App -c Release -p:Platform=<p> -p:GenerateAppxPackageOnBuild=true
                          -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never -p:WindowsAppSDKSelfContained=<true|false>
                          (the same switches CI and scripts/build.ps1 -Msix use). The app project's obj\ and bin\ for that platform are
                          deleted first: a release is always a clean build.
    2. If -PfxPath is given, sign each per-architecture .msix, so the packages inside the bundle carry the same publisher signature.
    3. Copy each (signed) per-architecture package to artifacts\.
    4. makeappx bundle -> artifacts\*.msixbundle
    5. If -PfxPath is given, signtool sign the bundle (SHA-256; optional RFC 3161 timestamp).
  Then SHA256SUMS.txt.

  Unsigned bundles cannot be installed; they are what CI uploads. Sign with the dev cert (packaging\make-dev-cert.ps1) or your
  release certificate. The signing certificate subject must equal the manifest Publisher (CN=MikeShift Dev for the dev cert).

  makeappx.exe / signtool.exe are located in this order: -MakeAppx / -SignTool, the newest Windows 10 SDK under
  "%ProgramFiles(x86)%\Windows Kits\10\bin" (10.0.26100.* preferred), the Microsoft.Windows.SDK.BuildTools NuGet package in the
  global packages folder, then PATH.

.PARAMETER Configuration  Build configuration. Default Release.
.PARAMETER Platforms      Default x64, ARM64.
.PARAMETER Flavor         sideload (default), store or all. See above.
.PARAMETER OutDir         Output folder. Default <repo>\artifacts.
.PARAMETER PfxPath        Sign with this .pfx. Omit for an unsigned bundle.
.PARAMETER PfxPassword    Password for the .pfx (SecureString). Falls back to MIKESHIFT_PFX_PASSWORD.
.PARAMETER TimestampUrl   RFC 3161 timestamp server (e.g. http://timestamp.digicert.com). Optional: this is the only thing in the whole
                          pipeline that touches the network, and only when you ask for it.
.PARAMETER SkipBuild      Reuse the .msix files already in src\MikeShift.App\AppPackages\<flavour>.
.PARAMETER MakeAppx       Explicit path to makeappx.exe.
.PARAMETER SignTool       Explicit path to signtool.exe.

.EXAMPLE
  ./scripts/release.ps1
.EXAMPLE
  ./scripts/release.ps1 -Flavor all -PfxPath artifacts\dev-cert\MikeShiftDev.pfx -PfxPassword (Read-Host -AsSecureString 'PFX password')
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')] [string] $Configuration = 'Release',
    [ValidateSet('x64', 'ARM64')] [string[]] $Platforms = @('x64', 'ARM64'),
    [ValidateSet('sideload', 'store', 'all')] [string] $Flavor = 'sideload',
    [string] $OutDir,
    [string] $PfxPath,
    [securestring] $PfxPassword,
    [string] $TimestampUrl,
    [switch] $SkipBuild,
    [string] $MakeAppx,
    [string] $SignTool
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts' }

function Find-SdkTool([string] $exe) {
    $arch = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'arm64' } else { 'x64' }
    $candidates = @()

    # 1. Windows SDK. Newest first, but 10.0.26100.* (the version this repo targets) wins when present.
    $kits = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kits) {
        $versions = Get-ChildItem $kits -Directory | Where-Object { $_.Name -match '^10\.0\.\d+\.\d+$' } |
            Sort-Object { [version] $_.Name } -Descending
        $preferred = @($versions | Where-Object { $_.Name -like '10.0.26100.*' }) + @($versions | Where-Object { $_.Name -notlike '10.0.26100.*' })
        foreach ($v in $preferred) { $candidates += (Join-Path $v.FullName "$arch\$exe"); $candidates += (Join-Path $v.FullName "x64\$exe") }
    }

    # 2. Microsoft.Windows.SDK.BuildTools NuGet package (the app project already restores it, so it exists wherever the app builds).
    $pkgRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
    $bt = Join-Path $pkgRoot 'microsoft.windows.sdk.buildtools'
    if (Test-Path $bt) {
        Get-ChildItem $bt -Directory | Sort-Object { try { [version] ($_.Name -replace '-.*$', '') } catch { [version] '0.0' } } -Descending | ForEach-Object {
            $binDir = Join-Path $_.FullName 'bin'
            if (Test-Path $binDir) {
                Get-ChildItem $binDir -Directory | Sort-Object Name -Descending | ForEach-Object {
                    $candidates += (Join-Path $_.FullName "$arch\$exe"); $candidates += (Join-Path $_.FullName "x64\$exe")
                }
            }
        }
    }

    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }

    # 3. PATH
    $cmd = Get-Command $exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "$exe not found. Install the Windows 10/11 SDK (10.0.26100) or pass an explicit path."
}

function Invoke-Step([string] $title, [scriptblock] $block) {
    Write-Host "==> $title" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$title failed (exit code $LASTEXITCODE)" }
}

function Get-PlainPassword([securestring] $secure) {
    [System.Net.NetworkCredential]::new('', $secure).Password
}

Push-Location $root
try {
    # --- inputs -------------------------------------------------------------------------------------------------------------
    $manifestPath = Join-Path $root 'src\MikeShift.App\Package.appxmanifest'
    $identity = ([xml](Get-Content -LiteralPath $manifestPath -Raw)).Package.Identity
    $version = $identity.Version
    $publisher = $identity.Publisher
    Write-Host "MikeShift $version  publisher $publisher  platforms $($Platforms -join ', ')" -ForegroundColor Cyan

    $signing = [bool] $PfxPath
    if ($signing) {
        if (-not (Test-Path -LiteralPath $PfxPath)) { throw "PFX not found: $PfxPath" }
        if (-not $PfxPassword) {
            if ($env:MIKESHIFT_PFX_PASSWORD) { $PfxPassword = ConvertTo-SecureString $env:MIKESHIFT_PFX_PASSWORD -AsPlainText -Force }
            else { throw 'Signing needs -PfxPassword (SecureString) or MIKESHIFT_PFX_PASSWORD.' }
        }
        $PfxPath = (Resolve-Path -LiteralPath $PfxPath).Path
        $SignTool = if ($SignTool) { $SignTool } else { Find-SdkTool 'signtool.exe' }
    }
    $MakeAppx = if ($MakeAppx) { $MakeAppx } else { Find-SdkTool 'makeappx.exe' }
    Write-Host "makeappx: $MakeAppx"
    if ($signing) { Write-Host "signtool: $SignTool" }

    function Sign-File([string] $path) {
        $signArgs = @('sign', '/fd', 'SHA256', '/f', $PfxPath, '/p', (Get-PlainPassword $PfxPassword))
        if ($TimestampUrl) { $signArgs += @('/tr', $TimestampUrl, '/td', 'SHA256') }
        $signArgs += $path
        Invoke-Step "signtool sign $(Split-Path -Leaf $path)" { & $SignTool @signArgs }
    }

    # --- build, stage, (sign), bundle: once per flavour -----------------------------------------------------------------------
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $OutDir = (Resolve-Path -LiteralPath $OutDir).Path
    $flavors = if ($Flavor -eq 'all') { @('sideload', 'store') } else { @($Flavor) }
    $deliverables = @()

    foreach ($flavor in $flavors) {
        # sideload: Windows App SDK inside the package (nothing to install first). store: Windows App SDK as the shared framework package
        # Microsoft.WindowsAppRuntime.2 (the Microsoft Store installs it automatically; a sideloaded install needs it present).
        $selfContainedSdk = if ($flavor -eq 'store') { 'false' } else { 'true' }
        $tag = if ($flavor -eq 'store') { '_store' } else { '' }
        $packagesRoot = Join-Path $root "src\MikeShift.App\AppPackages\$flavor"
        Write-Host "==> flavour: $flavor (Windows App SDK $(if ($selfContainedSdk -eq 'true') { 'bundled' } else { 'shared framework' }))" -ForegroundColor Cyan

        $msixFiles = @()
        foreach ($p in $Platforms) {
            if (-not $SkipBuild) {
                # A release build is always a clean one for the app project (not Core or the CLI): switching the Windows App SDK mode
                # between builds in the same obj\ and bin\ would otherwise reuse a manifest and file lists of the other flavour.
                foreach ($dir in "src\MikeShift.App\obj\$p\$Configuration", "src\MikeShift.App\bin\$p\$Configuration") {
                    if (Test-Path -LiteralPath $dir) { Remove-Item -LiteralPath $dir -Recurse -Force }
                }
                if (Test-Path $packagesRoot) {
                    Get-ChildItem $packagesRoot -Directory -Filter "*_$($p)_Test" -ErrorAction SilentlyContinue |
                        ForEach-Object { Remove-Item -LiteralPath $_.FullName -Recurse -Force }
                }
                Invoke-Step "MSIX ($flavor, $p, $Configuration)" {
                    dotnet build src/MikeShift.App -c $Configuration "-p:Platform=$p" `
                        "-p:WindowsAppSDKSelfContained=$selfContainedSdk" "-p:AppxPackageDir=$packagesRoot\" `
                        -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false -p:AppxBundle=Never --nologo
                }
            }
            $found = @(Get-ChildItem $packagesRoot -Recurse -Filter "*_$($p).msix" -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -notmatch '\\Dependencies\\' } | Sort-Object LastWriteTime -Descending)
            if ($found.Count -eq 0) { throw "No .msix for $flavor/$p found under $packagesRoot" }
            $msixFiles += $found[0].FullName
        }

        $staging = Join-Path $OutDir 'bundle-staging'
        if (Test-Path $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
        New-Item -ItemType Directory -Force -Path $staging | Out-Null

        # Standalone per-architecture packages (what a user downloads for one PC: half the size of the bundle).
        foreach ($m in $msixFiles) {
            $dest = Join-Path $staging (Split-Path -Leaf $m)
            Copy-Item -LiteralPath $m -Destination $dest
            if ($signing) { Sign-File $dest }
            $arch = if ((Split-Path -Leaf $m) -match '_(x64|arm64)\.msix$') { $Matches[1] } else { throw "Cannot tell the architecture of $m" }
            $standalonePath = Join-Path $OutDir "MikeShift_$version$($tag)_$arch.msix"
            if (Test-Path $standalonePath) { Remove-Item -LiteralPath $standalonePath -Force }
            Copy-Item -LiteralPath $dest -Destination $standalonePath
            $deliverables += $standalonePath
        }

        $bundlePath = Join-Path $OutDir "MikeShift_$version$tag.msixbundle"
        if (Test-Path $bundlePath) { Remove-Item -LiteralPath $bundlePath -Force }
        Invoke-Step 'makeappx bundle' { & $MakeAppx bundle /d $staging /p $bundlePath /bv $version /o }
        Remove-Item -LiteralPath $staging -Recurse -Force
        if ($signing) { Sign-File $bundlePath }
        $deliverables += $bundlePath
    }

    # --- checksums (every package in the output folder, so running one flavour after the other keeps both listed) ---------------
    $all = @(Get-ChildItem -LiteralPath $OutDir -File | Where-Object { $_.Name -match '^MikeShift_.*\.(msix|msixbundle)$' } | Sort-Object Name)
    $lines = foreach ($f in $all) {
        '{0}  {1}' -f (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $f.Name
    }
    $sumsPath = Join-Path $OutDir 'SHA256SUMS.txt'
    [System.IO.File]::WriteAllText($sumsPath, (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ''
    $state = if ($signing) { 'signed' } else { 'UNSIGNED - cannot be installed until signed' }
    foreach ($f in $deliverables) {
        Write-Host ('{0,-44} {1,7:N1} MB  {2}' -f (Split-Path -Leaf $f), ((Get-Item $f).Length / 1MB), $state) -ForegroundColor Green
    }
    Write-Host "Sums   : $sumsPath"
    if (-not $signing) {
        Write-Host 'Sign it with: ./scripts/release.ps1 -PfxPath <pfx> -PfxPassword (Read-Host -AsSecureString) -SkipBuild   (see packaging/make-dev-cert.ps1)'
    }
}
finally {
    Pop-Location
}
