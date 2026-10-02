<#
.SYNOPSIS
Fails when a link in the generated API reference does not resolve.

.DESCRIPTION
Reads every generated page under docs/api/ and checks each link in it:

- a link into the reference (relative, or root-relative under /api/) must name a page that was generated;
- no link may point at learn.microsoft.com for a PeachPDF, PeachDrawing or PeachImage type, which is where DefaultDocumentation
  sends a cross-reference it cannot find a page for, and which does not document them.

Links to other sites are left alone. A #fragment is not checked.

.PARAMETER ApiDocsPath
Path to the directory the reference was generated into (docs/api).
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ApiDocsPath
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path $ApiDocsPath).Path.TrimEnd('\', '/')
$pages = @(Get-ChildItem -Path $root -Recurse -Filter '*.md')
if ($pages.Count -eq 0) { throw "No .md files found under '$root'." }

$known = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($page in $pages) { [void]$known.Add($page.FullName.Substring($root.Length).Replace('\', '/')) }

# [text](destination 'title') - DefaultDocumentation titles every link it writes, and a destination never contains a space.
$link = [System.Text.RegularExpressions.Regex]::new("\]\(([^\s'()]+(?:\([^\s)]*\)[^\s'()]*)*)(?:\s+'[^']*')?\)")
$ownTypes = [System.Text.RegularExpressions.Regex]::new('learn\.microsoft\.com/en-us/dotnet/api/(peachpdf|peachdrawing|peachimage)[./]', 'IgnoreCase')

$broken = New-Object 'System.Collections.Generic.List[string]'
$checked = 0
foreach ($page in $pages) {
    $pageUrl = $page.FullName.Substring($root.Length).Replace('\', '/')
    $pageDir = $pageUrl.Substring(0, $pageUrl.LastIndexOf('/') + 1)
    $content = [System.IO.File]::ReadAllText($page.FullName)

    foreach ($m in $ownTypes.Matches($content)) {
        $broken.Add("${pageUrl}: links to learn.microsoft.com for one of our own types ($($m.Value))")
    }

    foreach ($m in $link.Matches($content)) {
        $dest = $m.Groups[1].Value
        if ($dest -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $dest.StartsWith('#')) { continue }
        $hash = $dest.IndexOf('#')
        if ($hash -ge 0) { $dest = $dest.Substring(0, $hash) }
        if ($dest.Length -eq 0) { continue }

        if ($dest.StartsWith('/api/')) { $target = $dest.Substring(4) }
        elseif ($dest.StartsWith('/')) { continue }
        else { $target = $pageDir + $dest }

        # The site serves a generated .md page as .html, so a link to the .md source is a link to nothing.
        if ($target.EndsWith('.md')) { $broken.Add("${pageUrl}: $($m.Groups[1].Value) names the .md source, not the .html page"); continue }

        # A bare directory is its index page.
        if ($target.EndsWith('/')) { $target += 'index.md' }
        elseif ($target.EndsWith('.html')) { $target = $target.Substring(0, $target.Length - 5) + '.md' }
        $target = [System.Uri]::UnescapeDataString($target)

        $checked++
        if (-not $known.Contains($target)) { $broken.Add("${pageUrl}: $($m.Groups[1].Value) -> $target not generated") }
    }
}

if ($broken.Count -gt 0) {
    $broken | Sort-Object -Unique | Select-Object -First 60 | ForEach-Object { Write-Output $_ }
    throw "Test-ApiDocLinks: $($broken.Count) broken link(s) in the API reference (first 60 shown above)."
}
Write-Output "Test-ApiDocLinks: $checked link(s) in $($pages.Count) page(s) resolve."
