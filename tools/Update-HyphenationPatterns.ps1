<#
.SYNOPSIS
Downloads all hyphenation pattern files from the CTAN hyph-utf8 package (mirrored on GitHub at
hyphenation/tex-hyphen), converts each to PeachPDF's plain-text pattern format, Brotli-compresses
it, and writes it to src/PeachPDF/Text/Resources/Patterns for embedding as a resource.

.DESCRIPTION
For each `hyph-<tag>.tex` file under the pinned commit's
`hyph-utf8/tex/generic/hyph-utf8/patterns/tex/` directory, this script:
  1. Downloads that .tex source (it carries a structured comment header with copyright/license/
     hyphenmin metadata) and the corresponding stripped-down `hyph-<tag>.pat.txt` from
     `hyph-utf8/tex/generic/hyph-utf8/patterns/txt/` (the actual Liang digit-pattern data,
     already free of TeX syntax).
  2. Parses the .tex header to recover title, copyright, license text, and hyphenation minimums
     (falling back to 2/3, TeX's own default, when a file doesn't state its own).
  3. Composes a human-readable pattern file in the same header style as the pre-existing
     hyph-en-us.txt, with an additional machine-parsed `# hyphenmins: left=N right=N` line.
  4. Brotli-compresses it to `hyph-<tag>.txt.br` in -OutputDirectory.

Tags with no matching .pat.txt (a handful of complex-script languages that don't use the
`\patterns{}` macro), and tags whose resolved license is GPL/LGPL family or explicitly
unstated, are skipped with a warning rather than included — PeachPDF ships only permissively
licensed (MIT/LPPL/BSD-style/public-domain) pattern data, consistent with the library's own
license. Review the skip warnings after any re-run in case upstream re-licenses a pattern file.

Requires PowerShell 7+ — Windows PowerShell 5.1 runs on .NET Framework, which has no
System.IO.Compression.BrotliStream.

.PARAMETER OutputDirectory
Where to write the compressed .txt.br pattern files. Defaults to
src/PeachPDF/Text/Resources/Patterns relative to this script's location.

.PARAMETER Ref
Commit SHA (or branch/tag) of hyphenation/tex-hyphen to fetch from, pinned by default so re-runs
are reproducible until deliberately bumped.
#>
#Requires -Version 7.0
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\PeachPDF\Text\Resources\Patterns'),
    [string]$Ref = '5684c0f51c0b81133db2efbe60a408b4155a3ff5'
)

$ErrorActionPreference = 'Stop'

$repoRawBase = "https://raw.githubusercontent.com/hyphenation/tex-hyphen/$Ref"
$apiBase = 'https://api.github.com/repos/hyphenation/tex-hyphen/contents/hyph-utf8/tex/generic/hyph-utf8'
$headers = @{ 'User-Agent' = 'PeachPDF-Update-HyphenationPatterns' }

function Get-DirectoryListing([string]$ApiPath) {
    $entries = Invoke-RestMethod -Uri "$apiBase/$ApiPath`?ref=$Ref" -Headers $headers
    return $entries | Where-Object { $_.type -eq 'file' } | ForEach-Object { $_.name }
}

# Parses the structured YAML-ish comment header that every hyph-utf8 `.tex` source carries
# (title/copyright/language/licence/hyphenmins fields, each line prefixed with '%'), stopping at
# the '% ====...' divider that marks the end of metadata and the start of the real TeX body.
function Parse-TexHeader([string]$TexContent) {
    $lines = $TexContent -split "`r?`n"

    $headerLines = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^%\s*=+\s*$') { break }
        if ($line -notmatch '^%') { break }
        $headerLines.Add(($line -replace '^%\s?', ''))
    }

    $result = @{
        Title       = $null
        Copyright   = $null
        LicenseText = $null
        Left        = 2
        Right       = 3
    }

    for ($i = 0; $i -lt $headerLines.Count; $i++) {
        $line = $headerLines[$i]

        if ($line -match '^title:\s*(.+)$') { $result.Title = $Matches[1].Trim(); continue }
        if ($line -match '^copyright:\s*(.+)$') { $result.Copyright = $Matches[1].Trim(); continue }

        if ($line -match '^licence:\s*$') {
            # Scope this section to its indented children only (until the next top-level,
            # unindented key), so "name:"/"url:" here can't accidentally pick up an unrelated
            # sibling section's fields (e.g. authors[].name, which uses the same sub-key names).
            $section = New-Object System.Collections.Generic.List[string]
            $j = $i + 1
            for (; $j -lt $headerLines.Count; $j++) {
                $next = $headerLines[$j]
                if ($next.Trim().Length -eq 0) { $section.Add($next); continue }
                if ($next -notmatch '^\s') { break }
                # Fields are sometimes written as a YAML list ("    - name: MIT") instead of
                # plain nested keys ("    name: MIT") - normalize away the "- " list marker
                # (preserving indentation) so both shapes hit the same field patterns below.
                $section.Add(($next -replace '^(\s*)-\s+', '$1'))
            }
            $i = $j - 1

            $licenceName = $null; $licenceVersion = $null; $licenceUrl = $null; $licenceOrLater = $false
            for ($k = 0; $k -lt $section.Count; $k++) {
                $sLine = $section[$k]
                if ($sLine -match '^\s*name:\s*(.+)$') { $licenceName = $Matches[1].Trim() }
                elseif ($sLine -match '^\s*version:\s*(.+)$') { $licenceVersion = $Matches[1].Trim() }
                elseif ($sLine -match '^\s*url:\s*(.+)$') { $licenceUrl = $Matches[1].Trim() }
                elseif ($sLine -match '^\s*or_later:\s*true\s*$') { $licenceOrLater = $true }
                elseif ($sLine -match '^\s*text:\s*\[None\]\s*$') {
                    $result.LicenseText = 'No license specified in the source file.'
                }
                elseif ($sLine -match '^\s*text:\s*[>|][-+]?\s*(#.*)?$') {
                    # YAML folded (">") or literal ("|"/"|-") block scalar: gather subsequent
                    # more-indented lines until dedent.
                    $blockIndent = $null
                    $textParts = New-Object System.Collections.Generic.List[string]
                    for ($n = $k + 1; $n -lt $section.Count; $n++) {
                        $next = $section[$n]
                        if ($next.Trim().Length -eq 0) { continue }
                        $indent = ($next -replace '^(\s*).*$', '$1').Length
                        if ($null -eq $blockIndent) { $blockIndent = $indent }
                        if ($indent -lt $blockIndent) { break }
                        $textParts.Add($next.Trim())
                        $k = $n
                    }
                    $result.LicenseText = ($textParts -join ' ')
                }
                elseif ($sLine -match '^\s*text:\s*(.+)$') {
                    $result.LicenseText = $Matches[1].Trim()
                }
            }

            if (-not $result.LicenseText -and $licenceName) {
                $parts = @($licenceName)
                if ($licenceVersion) { $parts += ($licenceVersion + $(if ($licenceOrLater) { ' or later' } else { '' })) }
                if ($licenceUrl) { $parts += ('— ' + $licenceUrl) }
                $result.LicenseText = ($parts -join ' ')
            }
            continue
        }

        if ($line -match '^\s*left:\s*(\d+)$') { $result.Left = [int]$Matches[1] }
        elseif ($line -match '^\s*right:\s*(\d+)$') { $result.Right = [int]$Matches[1] }
    }

    return $result
}

# PeachPDF ships only permissively licensed pattern data (MIT/LPPL/BSD-style/public-domain),
# consistent with the library's own license. A pattern set whose resolved license text is
# GPL/LGPL family, or explicitly states no license, is excluded rather than bundled.
function Test-PermissiveLicense([string]$LicenseText) {
    if ([string]::IsNullOrWhiteSpace($LicenseText)) { return $false }
    if ($LicenseText -match '\bno license specified\b') { return $false }
    if ($LicenseText -match '(?<!L)\bGPL\b' -or $LicenseText -match '\bLGPL\b') { return $false }
    return $true
}

if (-not (Test-Path $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
}

Write-Output "Listing pattern sources at ref $Ref..."
$texFiles = Get-DirectoryListing -ApiPath 'patterns/tex' | Where-Object { $_ -match '^hyph-.+\.tex$' }
$txtFiles = Get-DirectoryListing -ApiPath 'patterns/txt' | Where-Object { $_ -match '\.pat\.txt$' }
$txtFileSet = [System.Collections.Generic.HashSet[string]]::new([string[]]$txtFiles)

Write-Output "Found $($texFiles.Count) .tex source files, $($txtFiles.Count) .pat.txt data files."

$retrievedDate = (Get-Date).ToString('yyyy-MM-dd')
$processed = 0
$skipped = 0

foreach ($texFileName in $texFiles) {
    $tag = $texFileName -replace '^hyph-', '' -replace '\.tex$', ''
    $patTxtFileName = "hyph-$tag.pat.txt"

    if (-not $txtFileSet.Contains($patTxtFileName)) {
        Write-Warning "Skipping '$tag': no matching $patTxtFileName (likely a non-Liang-pattern script)."
        $skipped++
        continue
    }

    $texContent = Invoke-RestMethod -Uri "$repoRawBase/hyph-utf8/tex/generic/hyph-utf8/patterns/tex/$texFileName" -Headers $headers
    $patTxtContent = Invoke-RestMethod -Uri "$repoRawBase/hyph-utf8/tex/generic/hyph-utf8/patterns/txt/$patTxtFileName" -Headers $headers

    $meta = Parse-TexHeader -TexContent $texContent

    $patternLines = ($patTxtContent -split "`r?`n") | Where-Object { $_.Trim().Length -gt 0 }
    if ($patternLines.Count -eq 0) {
        Write-Warning "Skipping '$tag': $patTxtFileName had no pattern lines."
        $skipped++
        continue
    }

    if (-not (Test-PermissiveLicense $meta.LicenseText)) {
        Write-Warning "Skipping '$tag': non-permissive or unstated license ('$($meta.LicenseText)')."
        $skipped++
        continue
    }

    $title = if ($meta.Title) { $meta.Title } else { "Hyphenation patterns for $tag" }
    $copyright = if ($meta.Copyright) { $meta.Copyright } else { 'See hyph-utf8 project for copyright holder.' }

    $header = @"
# $title, Liang/Knuth-style pattern-based hyphenation.
#
# Source: hyph-$tag.tex from the hyph-utf8 package (https://github.com/hyphenation/tex-hyphen),
# retrieved $retrievedDate from commit $Ref.
#
# Copyright: $copyright
#
# License notice (preserved per the source file's terms): "$($meta.LicenseText)"
#
# Format: one pattern per line. A pattern is a run of letters with optional digit annotations
# between them (e.g. "hy1phen"), and an optional leading/trailing "." marking a word boundary.
# See HyphenationEngine for how these are applied (Liang's algorithm).
#
# hyphenmins: left=$($meta.Left) right=$($meta.Right)

"@

    $outputText = $header + ($patternLines -join "`n") + "`n"
    $textBytes = [System.Text.Encoding]::UTF8.GetBytes($outputText)

    $outputPath = Join-Path $OutputDirectory "hyph-$tag.txt.br"
    $fileStream = [System.IO.File]::Create($outputPath)
    try {
        $brotliStream = [System.IO.Compression.BrotliStream]::new($fileStream, [System.IO.Compression.CompressionLevel]::Optimal)
        try {
            $brotliStream.Write($textBytes, 0, $textBytes.Length)
        }
        finally {
            $brotliStream.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }

    $processed++
    Write-Output "  [$processed] $tag -> $(Split-Path $outputPath -Leaf) ($($patternLines.Count) patterns)"
}

Write-Output "Done. Wrote $processed pattern file(s) to '$OutputDirectory', skipped $skipped."
