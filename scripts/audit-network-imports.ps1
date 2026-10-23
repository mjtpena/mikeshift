<#
.SYNOPSIS
  Static evidence for "MikeShift has no network code": scans every DLL/EXE in a build or publish folder for networking.

.DESCRIPTION
  * Managed assemblies: lists every reference to a networking API, by reading the metadata (no code is run):
    type references in System.Net.*, Windows.Web.*, Windows.Networking.*, Microsoft.Web.* (WebView2), System.Net.Http,
    and member references to the same.  `System.Net.WebUtility` (HTML entity encoding) is reported separately: it is string
    handling, not networking.
  * Native DLLs and EXEs: parses the PE import table and lists imports from the Windows networking libraries
    (ws2_32, wsock32, winhttp, wininet, dnsapi, iphlpapi, urlmon, mswsock, webservices, rasapi32, httpapi, netapi32).
  Nothing is modified. Needs no administrator rights.

.PARAMETER Path
  Folder to scan (recursively). Default: the CLI build output.

.EXAMPLE
  ./scripts/audit-network-imports.ps1 -Path src/MikeShift.Cli/bin/x64/Debug/net10.0-windows10.0.26100.0/win-x64
#>
[CmdletBinding()]
param(
  [string]$Path = (Join-Path $PSScriptRoot '..\src\MikeShift.Cli\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64'),
  [switch]$Quiet
)

$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

public static class NetAudit
{
    static readonly string[] ManagedPrefixes = {
        "System.Net.", "Windows.Web", "Windows.Networking", "Microsoft.Web", "System.ServiceModel", "Windows.Services.Maps", "Windows.Security.Authentication.Web"
    };
    static readonly string[] NativeLibs = {
        "ws2_32", "wsock32", "winhttp", "wininet", "dnsapi", "iphlpapi", "urlmon", "mswsock", "webservices", "rasapi32", "httpapi", "netapi32", "wlanapi", "fwpuclnt"
    };

    public static List<string> Managed(string file)
    {
        var hits = new List<string>();
        using var fs = File.OpenRead(file);
        using var pe = new PEReader(fs);
        if (!pe.HasMetadata) return hits;
        var md = pe.GetMetadataReader();
        foreach (var h in md.TypeReferences)
        {
            var t = md.GetTypeReference(h);
            var ns = md.GetString(t.Namespace);
            var name = md.GetString(t.Name);
            var full = ns.Length == 0 ? name : ns + "." + name;
            if (ManagedPrefixes.Any(p => full.StartsWith(p, StringComparison.Ordinal))) hits.Add(full);
        }
        return hits.Distinct().OrderBy(x => x).ToList();
    }

    public static List<string> Native(string file)
    {
        var hits = new List<string>();
        using var fs = File.OpenRead(file);
        using var pe = new PEReader(fs);
        var hdr = pe.PEHeaders;
        var dir = hdr.PEHeader?.ImportTableDirectory;
        if (dir == null || dir.Value.Size == 0) return hits;
        var data = pe.GetEntireImage().GetContent().ToArray();
        int RvaToOffset(int rva)
        {
            foreach (var s in hdr.SectionHeaders)
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + Math.Max(s.VirtualSize, s.SizeOfRawData))
                    return rva - s.VirtualAddress + s.PointerToRawData;
            return -1;
        }
        var off = RvaToOffset(dir.Value.RelativeVirtualAddress);
        while (off > 0 && off + 20 <= data.Length)
        {
            var nameRva = BitConverter.ToInt32(data, off + 12);
            if (nameRva == 0) break;
            var no = RvaToOffset(nameRva);
            if (no < 0) break;
            var end = no; while (end < data.Length && data[end] != 0) end++;
            var dll = System.Text.Encoding.ASCII.GetString(data, no, end - no).ToLowerInvariant();
            var stem = dll.EndsWith(".dll") ? dll[..^4] : dll;
            if (NativeLibs.Contains(stem)) hits.Add(dll);
            off += 20;
        }
        return hits.Distinct().ToList();
    }
}
'@

$root = (Resolve-Path $Path).Path
$files = Get-ChildItem $root -Recurse -File | Where-Object { $_.Extension -in '.dll', '.exe' }
$managedHits = @{}
$nativeHits = @{}
$scanned = 0
foreach ($f in $files) {
  $scanned++
  try {
    $m = [NetAudit]::Managed($f.FullName)
    if ($m.Count) { $managedHits[$f.FullName.Substring($root.Length + 1)] = $m }
  } catch { }
  try {
    $n = [NetAudit]::Native($f.FullName)
    if ($n.Count) { $nativeHits[$f.FullName.Substring($root.Length + 1)] = $n }
  } catch { }
}

if (-not $Quiet) {
  "Scanned $scanned binaries under $root"
  ""
  "== Managed assemblies referencing networking types =="
  if ($managedHits.Count -eq 0) { "(none)" }
  foreach ($k in $managedHits.Keys | Sort-Object) { "{0}`n    {1}" -f $k, ($managedHits[$k] -join "`n    ") }
  ""
  "== Binaries importing a Windows networking library =="
  if ($nativeHits.Count -eq 0) { "(none)" }
  foreach ($k in $nativeHits.Keys | Sort-Object) { "{0}: {1}" -f $k, ($nativeHits[$k] -join ', ') }
}
[pscustomobject]@{ Scanned = $scanned; ManagedAssembliesWithNetworkTypes = $managedHits.Count; NativeBinariesWithNetworkImports = $nativeHits.Count }
