# Releasing

How a MikeShift release is built, checked, signed and submitted. For how the package is put together see [architecture §7](architecture.md#7-packaging); for the checks to run first see [testing](testing.md).

## Flavours and packages

MikeShift ships as MSIX packages, one per architecture (x64 and ARM64), IL-trimmed, with the .NET runtime inside the package (an MSIX cannot depend on a machine-wide .NET). There are two flavours that run the same managed code and differ only in where WinUI's native files come from:

| Flavour | `-Flavor` | Windows App SDK | Size per architecture | For |
|---|---|---|---|---|
| **sideload** (default) | `sideload` | inside the package | about 45 MB | Anything you hand to someone: nothing to install first. |
| **store** | `store` | the shared framework package `Microsoft.WindowsAppRuntime.2` | about 23 MB | The Microsoft Store, which installs the framework itself. A sideloaded Store-flavour package needs the framework installed first (Microsoft's `WindowsAppRuntimeInstall-<arch>.exe`, or `Add-AppxPackage -DependencyPath`). |

## Making the packages

Prerequisites: the .NET 10 SDK and the Windows 10/11 SDK 10.0.26100 (for `makeappx` and `signtool`; `release.ps1` also finds them in the `Microsoft.Windows.SDK.BuildTools` NuGet package).

```powershell
./scripts/release.ps1                     # sideload packages, unsigned: artifacts\MikeShift_<version>_x64.msix, _arm64.msix, .msixbundle, SHA256SUMS.txt
./scripts/release.ps1 -Flavor store       # the Store flavour: ..._store_x64.msix, _store_arm64.msix, _store.msixbundle
./scripts/release.ps1 -Flavor all         # both
./scripts/release.ps1 -Platforms x64      # one architecture only
```

For each flavour and architecture the script does a **clean** build of the app (its `obj\` and `bin\` are deleted first, because the Windows App SDK mode changes the manifest and the file list, and an incremental build would mix the two), writes each architecture as its own `.msix`, combines both into one `.msixbundle` with `makeappx bundle`, signs them if given a certificate, and writes `SHA256SUMS.txt` (in `sha256sum` format) for every package in the folder.

| Parameter | Meaning |
|---|---|
| `-Configuration` | `Release` (default) or `Debug`. |
| `-Platforms` | `x64`, `ARM64`; default both. |
| `-Flavor` | `sideload` (default), `store` or `all`. |
| `-OutDir` | Output folder; default `artifacts\`. |
| `-PfxPath`, `-PfxPassword` | Sign with this `.pfx` (password as a `SecureString`, or the `MIKESHIFT_PFX_PASSWORD` environment variable). Omit for unsigned packages. |
| `-TimestampUrl` | An RFC 3161 timestamp server. This is the only thing in the pipeline that touches the network, and only when you ask for it. |
| `-SkipBuild` | Reuse the `.msix` files already in `src\MikeShift.App\AppPackages\<flavour>`. |
| `-MakeAppx`, `-SignTool` | Explicit tool paths. |

`./scripts/build.ps1 -Configuration Release -Test -Bundle` builds, tests and then runs the release script. `-p:MikeShiftTrim=false` gives an untrimmed package for debugging; `-p:BundleCli=false` leaves the command-line tool out for faster inner-loop builds.

CI (`.github/workflows/ci.yml`) runs `release.ps1 -Flavor all` unsigned on every push to `main` or a `feature/` branch and on pull requests, and uploads the result. Those packages **cannot be installed**; they prove that packaging works.

## Versioning

* The product version is set in `Directory.Build.props` (`<Version>`) and the package version in `src/MikeShift.App/Package.appxmanifest` (`Identity Version`, four parts). Keep them in step.
* The Microsoft Store requires the last part of a package version to be `0` (use `1.0.0.0` for 1.0.0).
* Update [CHANGELOG.md](../../CHANGELOG.md) in the same commit: move the "unreleased" notes under the new version and date.

## Signing

Windows installs an MSIX only if it trusts the certificate that signed it, and the certificate's subject must equal the manifest's `Publisher`.

* **Development and testing (a certificate you make yourself).** The manifest's `Publisher` is `CN=MikeShift Dev`.
  ```powershell
  ./packaging/make-dev-cert.ps1 -Password (Read-Host -AsSecureString 'PFX password')   # artifacts\dev-cert\MikeShiftDev.pfx and .cer; nothing is installed machine-wide
  Import-Certificate -FilePath .\artifacts\dev-cert\MikeShiftDev.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople   # ELEVATED, once
  ./scripts/release.ps1 -PfxPath .\artifacts\dev-cert\MikeShiftDev.pfx -PfxPassword (Read-Host -AsSecureString 'PFX password')
  Add-AppxPackage -Path .\artifacts\MikeShift_<version>_x64.msix
  ```
  The `.pfx` is a private key: keep it out of git (the repository ignores `*.pfx` and `*.cer`). To remove the trust again: `Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Subject -eq 'CN=MikeShift Dev' | Remove-Item`.
* **A public release outside the Store** needs a certificate Windows already trusts (a purchased code-signing certificate, or Azure Trusted Signing) and a manifest `Publisher` that matches it. Change `Publisher` first, rebuild, then sign with `-PfxPath` (and `-TimestampUrl`, so the signature outlives the certificate).
* **The Microsoft Store** signs the package itself. Associating the app with the Store changes the package identity (`Name` and `Publisher`) to the values Partner Center assigns.

## Before tagging a release

1. `dotnet build MikeShift.sln` is at 0 warnings and `dotnet test MikeShift.sln` is green; run the slow suite too (`MIKESHIFT_SLOW=1`).
2. `./scripts/verify-offline.ps1` exits 0, and the audit script shows no networking imports ([offline verification](testing.md#offline-verification)).
3. Build both flavours and both architectures (`release.ps1 -Flavor all`), then run the [packaging checks](testing.md#packaging-checks) on the trimmed packages: the CLI suites against the trimmed executable, `--smoke-test`, the install and uninstall round trip, the alias in a fresh terminal.
4. Work through the [manual checklist](testing.md#manual-checklist) on Windows 11, and on Windows 10 and ARM64 if you are going to say they are supported.
5. Update the version, the changelog and any numbers quoted in the README (package sizes, memory) from the packages you just built.
6. Tag the commit, and publish the signed packages with `SHA256SUMS.txt` on the GitHub releases page (the app's **Check for updates** opens `https://github.com/mjtpena/mikeshift/releases`).

## Microsoft Store submission

All the text to paste is in [docs/store/](../store/listing.md); this is the order to do it in.

1. **Reserve the name** in Partner Center ("MikeShift: offline file converter", or "MikeShift"); run a trademark search first.
2. **Associate the app with the Store** (Visual Studio's *Associate App with the Store*, or edit `Identity` by hand to the values Partner Center shows) and set the package version to `x.y.z.0`.
3. **Build the Store flavour:** `./scripts/release.ps1 -Flavor store`, and upload the `_store.msixbundle` (or both `_store_` `.msix` files). Do not sign it: the Store does.
4. **Host the privacy statement** at a public URL, [docs/privacy.md](../privacy.md) rendered (the repository page or GitHub Pages), and paste the URL under Properties → Privacy policy. [docs/store/privacy-policy.md](../store/privacy-policy.md) says the same.
5. **Fill in the listing** from [listing.md](../store/listing.md): name, short and long description, what's new, features, search terms, category, the age-rating answers, the system requirements and the notes for certification. Fields in Partner Center change over time; the form is authoritative.
6. **Upload the screenshots** from [docs/media/store/](../media/store) in the order [screenshots.md](../store/screenshots.md) suggests, and the trailer if you have one ([demo-script.md](../store/demo-script.md)).
7. **Run the Windows App Certification Kit** on the Store package and fix what it finds.
8. **Decide the Windows 10 question.** The package declares Windows 10 1809 as the minimum, but the right-click entry has not been seen on Windows 10 ([limitations](../limitations.md#the-right-click-entry)). Either keep 1809 and keep the honest note in the description, or raise the minimum until a Windows 10 PC has been tried.
9. **Submit.** After certification, check the listing on a clean machine and install from the Store once.
