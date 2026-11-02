<#
.SYNOPSIS
  Renders .github/conformance/sample.html at every PDF/A level and validates each file with veraPDF.

.DESCRIPTION
  PDF/A conformance is checked by a real validator, not by asserting on the bytes PeachPDF writes: the unit tests prove
  what we intended, veraPDF proves what a conforming reader would accept. Every level the library offers is rendered by the
  built `peachpdf` CLI (Release - a Debug build writes a `% comment` after every `obj` keyword, which ISO 19005 rule
  6.1.8 rejects) and validated against its own veraPDF profile; the script fails if any level does not PASS.

  PDF/X has no veraPDF profile, so it is not covered here.

.PARAMETER VeraPdf
  Path to the veraPDF command-line launcher (`verapdf` / `verapdf.bat`).

.PARAMETER CliDll
  Path to the built PeachPDF.Cli.dll (Release).

.PARAMETER OutDir
  Where the PDFs and veraPDF XML reports go; kept as CI artifacts.
#>
param(
    [Parameter(Mandatory)] [string] $VeraPdf,
    [Parameter(Mandatory)] [string] $CliDll,
    [string] $OutDir = (Join-Path ([System.IO.Path]::GetTempPath()) 'peachpdf-conformance')
)

$ErrorActionPreference = 'Stop'

$conformance = Join-Path (Split-Path -Parent $PSScriptRoot) 'conformance'
$sample = (Resolve-Path (Join-Path $conformance 'sample.html')).Path
$attachment = (Resolve-Path (Join-Path $conformance 'attachment.csv')).Path
New-Item -ItemType Directory -Force $OutDir | Out-Null

# Level -> extra CLI arguments. PDF/A-1 forbids transparency, so it is flattened to bitmaps; the levels that require or
# permit embedded files get the attachment (PDF/A-4f requires one).
$levels = [ordered]@{
    '1a' = @('--flatten-transparency')
    '1b' = @('--flatten-transparency')
    '2a' = @()
    '2b' = @()
    '2u' = @()
    '3a' = @("--attach=$attachment;rel=data")
    '3b' = @("--attach=$attachment;rel=data")
    '3u' = @("--attach=$attachment;rel=data")
    '4'  = @()
    '4e' = @()
    '4f' = @("--attach=$attachment;rel=data")
}

$failed = @()
foreach ($level in $levels.Keys) {
    $pdf = Join-Path $OutDir "sample-$level.pdf"
    $report = Join-Path $OutDir "sample-$level.xml"

    & dotnet $CliDll $sample "--pdfa=$level" '--pdf-creation-date=2026-01-01' @($levels[$level]) -o $pdf
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error::peachpdf failed to render PDF/A-$level (exit code $LASTEXITCODE)"
        $failed += $level
        continue
    }

    & $VeraPdf --flavour $level --format xml $pdf | Out-File -Encoding utf8 $report
    $text = (& $VeraPdf --flavour $level --format text $pdf) -join "`n"
    Write-Host $text
    if ($text -notmatch '^PASS') {
        Write-Host "::error::PDF/A-$level output does not validate; see $report"
        $failed += $level
    }
}

# PDF/UA: the accessible-document sample (headings, landmarks, a figure, lists, a table with headers, links, a labelled field) at both parts, on
# its own and combined with the PDF/A level defined against the same PDF version. A field is only a widget with interactive forms on.
$uaSample = (Resolve-Path (Join-Path $conformance 'pdfua-sample.html')).Path
$uaCases = [ordered]@{
    'ua1'      = @{ Flavours = @('ua1'); Args = @('--pdfua=1') }
    'ua2'      = @{ Flavours = @('ua2'); Args = @('--pdfua=2') }
    'ua1-2a'   = @{ Flavours = @('ua1', '2a'); Args = @('--pdfua=1', '--pdfa=2a') }
    'ua2-4'    = @{ Flavours = @('ua2', '4'); Args = @('--pdfua=2', '--pdfa=4') }
}
foreach ($case in $uaCases.Keys) {
    $pdf = Join-Path $OutDir "pdfua-$case.pdf"
    & dotnet $CliDll $uaSample '--pdf-creation-date=2026-01-01' @($uaCases[$case].Args) -o $pdf
    if ($LASTEXITCODE -ne 0) {
        Write-Host "::error::peachpdf failed to render $case (exit code $LASTEXITCODE)"
        $failed += $case
        continue
    }

    foreach ($flavour in $uaCases[$case].Flavours) {
        $report = Join-Path $OutDir "pdfua-$case-$flavour.xml"
        & $VeraPdf --flavour $flavour --format xml $pdf | Out-File -Encoding utf8 $report
        $text = (& $VeraPdf --flavour $flavour --format text $pdf) -join "`n"
        Write-Host $text
        if ($text -notmatch '^PASS') {
            Write-Host "::error::$case output does not validate against $flavour; see $report"
            $failed += "$case/$flavour"
        }
    }
}

if ($failed.Count -gt 0) {
    Write-Host "Failed: $($failed -join ', ')"
    exit 1
}

Write-Host "All $($levels.Count) PDF/A levels and $($uaCases.Count) PDF/UA cases validate."
