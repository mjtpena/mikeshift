<#
.SYNOPSIS
  Creates a self-signed code-signing certificate for sideloading MikeShift, and exports it as .pfx + .cer.

.DESCRIPTION
  The certificate subject must equal the Publisher in src/MikeShift.App/Package.appxmanifest (the Store publisher ID); an MSIX
  signed with any other subject is rejected at install time.

  What this script does:
    1. Creates the certificate in the CurrentUser\My store (needed by New-SelfSignedCertificate; no admin rights, nothing machine-wide).
    2. Exports <OutDir>\MikeShiftDev.pfx (private key, protected by -Password) and <OutDir>\MikeShiftDev.cer (public part only).
    3. Removes the certificate from CurrentUser\My again (unless -KeepInStore), so the files are the only copy.
    4. Prints the one-time, elevated command that makes Windows trust the .cer.

  What it never does: install anything into LocalMachine stores. You run the printed Import-Certificate line yourself.

  The .pfx is a private key: keep it out of git (the repo ignores *.pfx and *.cer; the default OutDir is under artifacts/, also ignored).

.PARAMETER Password  Password for the .pfx (SecureString). If omitted, the MIKESHIFT_PFX_PASSWORD environment variable is used.
.PARAMETER OutDir    Where to write the .pfx and .cer. Default: <repo>\artifacts\dev-cert.
.PARAMETER Subject   Certificate subject. Default "CN=C81A4E31-21AC-4917-B3E7-F1C28300F48A" (must match the manifest Publisher).
.PARAMETER Years     Validity in years. Default 3.
.PARAMETER Name      File name stem for the exported files. Default MikeShiftDev.
.PARAMETER KeepInStore  Leave the certificate in CurrentUser\My after exporting.

.EXAMPLE
  ./packaging/make-dev-cert.ps1 -Password (Read-Host -AsSecureString 'PFX password')
.EXAMPLE
  ./packaging/make-dev-cert.ps1 -Password (ConvertTo-SecureString 'throwaway' -AsPlainText -Force)
#>
[CmdletBinding()]
param(
    [securestring] $Password,
    [string] $OutDir,
    [string] $Subject = 'CN=C81A4E31-21AC-4917-B3E7-F1C28300F48A',
    [ValidateRange(1, 20)] [int] $Years = 3,
    [string] $Name = 'MikeShiftDev',
    [switch] $KeepInStore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\dev-cert' }

if (-not $Password) {
    if ($env:MIKESHIFT_PFX_PASSWORD) {
        $Password = ConvertTo-SecureString $env:MIKESHIFT_PFX_PASSWORD -AsPlainText -Force
    }
    else {
        throw 'Provide -Password (a SecureString) or set MIKESHIFT_PFX_PASSWORD.'
    }
}

# Refuse to drift away from the manifest, which would produce a package that cannot be installed.
$manifest = Join-Path $root 'src\MikeShift.App\Package.appxmanifest'
if (Test-Path $manifest) {
    $publisher = ([xml](Get-Content -LiteralPath $manifest -Raw)).Package.Identity.Publisher
    if ($publisher -and ($publisher -ne $Subject)) {
        Write-Warning "Package.appxmanifest Publisher is '$publisher' but the certificate subject is '$Subject'. They must be identical."
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$pfxPath = Join-Path $OutDir "$Name.pfx"
$cerPath = Join-Path $OutDir "$Name.cer"

Write-Host "==> Creating self-signed code-signing certificate '$Subject' (CurrentUser\My)" -ForegroundColor Cyan
$cert = New-SelfSignedCertificate `
    -Type Custom `
    -Subject $Subject `
    -FriendlyName 'MikeShift Dev (sideload signing)' `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years) `
    -CertStoreLocation 'Cert:\CurrentUser\My' `
    -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')   # EKU = Code Signing, not a CA

try {
    Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $Password | Out-Null
    Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
}
finally {
    if (-not $KeepInStore) {
        Remove-Item -LiteralPath ("Cert:\CurrentUser\My\" + $cert.Thumbprint) -DeleteKey -ErrorAction SilentlyContinue
    }
}

Write-Host ''
Write-Host "Thumbprint : $($cert.Thumbprint)"
Write-Host "Subject    : $($cert.Subject)"
Write-Host "Expires    : $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host "PFX        : $pfxPath   (private key - do not commit or share)" -ForegroundColor Yellow
Write-Host "CER        : $cerPath   (public; give this to machines that should trust your builds)"
Write-Host ''
Write-Host 'One-time trust step on every machine that will install the package (elevated PowerShell):' -ForegroundColor Green
Write-Host "  Import-Certificate -FilePath '$cerPath' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host ''
Write-Host 'Then build + sign + install:' -ForegroundColor Green
Write-Host "  ./scripts/release.ps1 -PfxPath '$pfxPath' -PfxPassword (Read-Host -AsSecureString 'PFX password')"
Write-Host '  Add-AppxPackage -Path .\artifacts\MikeShift_<version>.msixbundle'
