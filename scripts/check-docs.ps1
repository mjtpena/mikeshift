<#
.SYNOPSIS
  Checks that every relative link and image in the repository's Markdown files points at something that exists.

.DESCRIPTION
  For each tracked-looking *.md file (bin, obj, artifacts, node_modules and .git are skipped) it reads
    * Markdown links and images: [text](target) and ![alt](target)
    * HTML images and links: <img src="target"> and <a href="target">
  outside fenced code blocks and inline code, and for every relative target checks that the file or folder exists. A target with a
  #fragment into a Markdown file is also checked against that file's headings (GitHub's slug rules: lower case, punctuation removed,
  spaces become hyphens). http(s), mailto and bare #fragment links to the same file are checked for the fragment only; web addresses
  are never fetched.

  Exit code 0 when everything resolves, 1 otherwise. Needs no network and no administrator rights.

.EXAMPLE
  ./scripts/check-docs.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

function Get-Slug([string] $heading) {
    $h = $heading.Trim().ToLowerInvariant()
    $h = $h -replace '`', ''
    $h = $h -replace '\[([^\]]*)\]\([^)]*\)', '$1'          # [text](url) -> text
    $h = [regex]::Replace($h, '[^\p{L}\p{N}\s_-]', '')       # drop punctuation
    return ($h -replace '\s', '-')
}

$headingCache = @{}
function Get-Anchors([string] $file) {
    if ($headingCache.ContainsKey($file)) { return $headingCache[$file] }
    $slugs = New-Object System.Collections.Generic.HashSet[string]
    $counts = @{}
    $inFence = $false
    foreach ($line in Get-Content -LiteralPath $file -Encoding UTF8) {
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        if ($line -match '^\s{0,3}#{1,6}\s+(.*?)\s*#*\s*$') {
            $slug = Get-Slug $Matches[1]
            if ($counts.ContainsKey($slug)) { $counts[$slug]++; [void]$slugs.Add("$slug-$($counts[$slug])") }
            else { $counts[$slug] = 0; [void]$slugs.Add($slug) }
        }
    }
    $headingCache[$file] = $slugs
    return $slugs
}

$files = Get-ChildItem -LiteralPath $root -Recurse -File -Filter '*.md' |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj|artifacts|node_modules|\.git)[\\/]' }

$problems = 0
$checked = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($root.Length + 1)
    $inFence = $false
    $n = 0
    foreach ($line in Get-Content -LiteralPath $f.FullName -Encoding UTF8) {
        $n++
        if ($line -match '^\s*(```|~~~)') { $inFence = -not $inFence; continue }
        if ($inFence) { continue }
        $text = [regex]::Replace($line, '`[^`]*`', '')       # ignore inline code
        $targets = @()
        foreach ($m in [regex]::Matches($text, '!?\[[^\]]*\]\(\s*<?([^)\s>]+)>?(?:\s+"[^"]*")?\s*\)')) { $targets += $m.Groups[1].Value }
        foreach ($m in [regex]::Matches($text, '<(?:img|a)\b[^>]*?\b(?:src|href)\s*=\s*"([^"]+)"', 'IgnoreCase')) { $targets += $m.Groups[1].Value }
        foreach ($t in $targets) {
            if ($t -match '^(https?:|mailto:)') { continue }
            $checked++
            $path, $frag = $t -split '#', 2
            $path = [uri]::UnescapeDataString($path)
            if ($path -eq '') { $dest = $f.FullName }
            elseif ($path.StartsWith('/')) { $dest = Join-Path $root $path.TrimStart('/') }
            else { $dest = Join-Path $f.DirectoryName $path }
            if (-not (Test-Path -LiteralPath $dest)) {
                Write-Host "BROKEN  ${rel}:$n  $t" -ForegroundColor Red
                $problems++
                continue
            }
            if ($frag -and $dest -like '*.md' -and (Test-Path -LiteralPath $dest -PathType Leaf)) {
                if (-not (Get-Anchors $dest).Contains($frag.ToLowerInvariant())) {
                    Write-Host "NO ANCHOR  ${rel}:$n  $t" -ForegroundColor Red
                    $problems++
                }
            }
        }
    }
}

Write-Host "$($files.Count) Markdown files, $checked relative links checked, $problems problem(s)."
if ($problems -gt 0) { exit 1 }
