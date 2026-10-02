<#
.SYNOPSIS
Rewrites internal cross-links in DefaultDocumentation's generated Markdown so they resolve on the
Jekyll-built docs site.

.DESCRIPTION
DefaultDocumentation emits links like `[Foo](Foo.md 'Foo')`, relying on the `jekyll-relative-links`
plugin to rewrite `.md` targets to the site's actual `.html` output at build time. That plugin's link
regex doesn't handle single-quoted titles (which DefaultDocumentation always emits) or destinations
containing literal parentheses (which method-signature-derived filenames like
`PdfGenerator.AddFontFamilyMapping(string,string).md` always do), so those links are left completely
unrewritten and 404 on the live site.

Since every possible link target is a known file in the same output directory, this script resolves
them itself: for each generated `.md` file, it swaps any link destination that exactly matches another
generated file's name from `.md` to `.html`, preserving any `#fragment` and the (untouched) title.
External links (e.g. to learn.microsoft.com) are left alone since they never match a local filename.

.PARAMETER ApiDocsPath
Path to the directory containing the generated Markdown files (e.g. docs/api).

.PARAMETER UnlinkLearnPrefix
Namespace prefix (e.g. PeachImage) whose cross-references that DefaultDocumentation sent to learn.microsoft.com, because it found no
page for the target, are turned back into plain text. Such a link can never be right - Microsoft does not document the package - and
the cause is a reference from a public member's documentation to something that is not public (so has no page). Used for a package
whose source is not in this repository and so cannot be corrected; for the packages here the fix is in the source, and
Test-ApiDocLinks.ps1 fails the build until it is made.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ApiDocsPath,

    [string]$UnlinkLearnPrefix
)

$ErrorActionPreference = 'Stop'

$files = [System.IO.Directory]::GetFiles($ApiDocsPath, '*.md')
if ($files.Count -eq 0) {
    throw "No .md files found in '$ApiDocsPath'."
}

$names = $files | ForEach-Object { [System.IO.Path]::GetFileName($_) }

$modifiedCount = 0
foreach ($filePath in $files) {
    $content = [System.IO.File]::ReadAllText($filePath)
    $original = $content

    foreach ($name in $names) {
        $htmlName = $name.Substring(0, $name.Length - 3) + '.html'
        $escaped = [System.Text.RegularExpressions.Regex]::Escape($name)
        # Matches ](KNOWNFILE.md  optional #fragment  optional ' title' )
        $pattern = "\]\($escaped(#[^\s')]*)?(\s+'[^']*')?\)"
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, $pattern, {
            param($m)
            "]($htmlName$($m.Groups[1].Value)$($m.Groups[2].Value))"
        })
    }

    # A link into another package's pages comes from the links file DefaultDocumentation read (src/ApiReferenceLinks.props), which
    # names its pages as .md; the site serves them as .html. Root-relative, so no known-file list is needed to recognise one.
    $content = [System.Text.RegularExpressions.Regex]::Replace($content, "\]\((/api/[^\s']*?)\.md(#[^\s']*)?(\s+'[^']*')?\)", {
        param($m)
        "]($($m.Groups[1].Value).html$($m.Groups[2].Value)$($m.Groups[3].Value))"
    })

    if ($UnlinkLearnPrefix) {
        $unlink = "\[([^\]]+)\]\(https://learn\.microsoft\.com/en-us/dotnet/api/$($UnlinkLearnPrefix.ToLowerInvariant())[^\s']*\s+'[^']*'\)"
        $content = [System.Text.RegularExpressions.Regex]::Replace($content, $unlink, '$1')
    }

    if ($content -ne $original) {
        $modifiedCount++
        [System.IO.File]::WriteAllText($filePath, $content)
    }
}

Write-Output "Fix-ApiDocLinks: rewrote internal links in $modifiedCount / $($files.Count) file(s) under '$ApiDocsPath'."
