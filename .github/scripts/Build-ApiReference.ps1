<#
.SYNOPSIS
Generates the whole API reference under docs/api/ - the PeachDrawing packages, PeachImage and PeachPDF - with the
pages of each package linking to the pages of the packages it uses.

.DESCRIPTION
Each package is documented with DefaultDocumentation, which writes a links file naming each of the package's pages and
reads the links files of the packages built before it (see src/ApiReferenceLinks.props). So the packages are built in the
order of the references between them:

  PeachDrawing.Text -> PeachDrawing.Core -> PeachImage -> PeachDrawing -> PeachPDF

PeachImage is not part of this repository. Its documentation is generated from the tag of the version PeachPDF's
PackageReference names, cloned into artifacts/, so the reference describes the PeachImage PeachPDF was actually built with.
Its project does not reference DefaultDocumentation, so the tool is run directly over the built assembly and its XML
documentation, with the same settings src/ApiReference.props gives the packages in this repository.

PeachPDF's pages stay at the root of docs/api/ so existing links to them stay valid; every other package has a folder of its
own, because the generated page names are not qualified by assembly. Finally every link in the generated pages is checked
(Test-ApiDocLinks.ps1), so a link that does not resolve fails the build rather than reaching the site.

.PARAMETER SkipLinkCheck
Generate without running the link check.
#>
param(
    [switch]$SkipLinkCheck
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$src = Join-Path $repoRoot 'src'
$apiRoot = Join-Path $repoRoot 'docs/api'
$linksFolder = Join-Path $repoRoot 'artifacts/api-links'
$fixLinks = Join-Path $PSScriptRoot 'Fix-ApiDocLinks.ps1'

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    # Windows PowerShell turns a native command's stderr into a terminating error under 'Stop'; the exit code is the verdict.
    $ErrorActionPreference = 'Continue'
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "'$Command $($Arguments -join ' ')' failed with exit code $LASTEXITCODE." }
}

function Invoke-PackageReference {
    param([string]$Project)
    Invoke-Checked 'dotnet' @('build', (Join-Path $src "$Project/$Project.csproj"), '--configuration', 'Release',
        '-p:TargetFramework=net10.0', '-p:DisableDefaultDocumentation=false', '--nologo', '-v:q')
}

# The version of PeachImage PeachPDF is built with, and the DefaultDocumentation the rest of the reference is built with.
$peachPdfProject = Get-Content -Raw (Join-Path $src 'PeachPDF/PeachPDF.csproj')
if ($peachPdfProject -notmatch 'Include="PeachImage"\s+Version="([^"]+)"') { throw 'PeachPDF.csproj has no PeachImage PackageReference.' }
$peachImageVersion = $Matches[1]
$apiProps = Get-Content -Raw (Join-Path $src 'ApiReference.props')
if ($apiProps -notmatch 'Include="DefaultDocumentation"\s+Version="([^"]+)"') { throw 'ApiReference.props has no DefaultDocumentation PackageReference.' }
$defaultDocumentationVersion = $Matches[1]

# The guide page names the version, from the same place the reference's version comes from.
[System.IO.File]::WriteAllText((Join-Path $repoRoot 'docs/_data/peachimage.yml'), "version: ""$peachImageVersion""`n")

if (Test-Path $apiRoot) { Remove-Item -Recurse -Force $apiRoot }
if (Test-Path $linksFolder) { Remove-Item -Recurse -Force $linksFolder }
New-Item -ItemType Directory -Force $apiRoot, $linksFolder | Out-Null

function Invoke-PeachImageReference {
    $source = Join-Path $repoRoot 'artifacts/PeachImage-src'
    if (Test-Path $source) { Remove-Item -Recurse -Force $source }
    Invoke-Checked 'git' @('clone', '--quiet', '--depth', '1', '--branch', "v$peachImageVersion", 'https://github.com/jhaygood86/PeachImage.git', $source)

    # PeachImage pins an SDK feature band of its own; the SDK this build already has is enough to compile it. Its analyzer
    # settings are for its own CI, and a warning there is not a reason to lose the documentation.
    Remove-Item -Force (Join-Path $source 'global.json') -ErrorAction SilentlyContinue
    $project = Join-Path $source 'src/PeachImage/PeachImage.csproj'
    Invoke-Checked 'dotnet' @('build', $project, '--configuration', 'Release', '--framework', 'net10.0',
        '-p:TreatWarningsAsErrors=false', '-p:EnforceCodeStyleInBuild=false', '--nologo', '-v:q')

    $packages = (& dotnet nuget locals global-packages --list | Select-Object -First 1) -replace '^.*global-packages:\s*', ''
    $toolRoot = Join-Path $packages "defaultdocumentation/$defaultDocumentationVersion/tools"
    $console = Get-ChildItem -Path $toolRoot -Recurse -Filter 'DefaultDocumentation.Console.dll' |
        Where-Object { $_.FullName -match '[\\/]net10\.0[\\/]' } | Select-Object -First 1
    if (-not $console) { throw "DefaultDocumentation $defaultDocumentationVersion is not in the NuGet cache ($toolRoot); build a package that references it first." }

    $output = Join-Path $apiRoot 'PeachImage'
    $built = Join-Path $source 'src/PeachImage/bin/Release/net10.0'
    Invoke-Checked 'dotnet' @($console.FullName,
        '--AssemblyFilePath', (Join-Path $built 'PeachImage.dll'),
        '--DocumentationFilePath', (Join-Path $built 'PeachImage.xml'),
        '--ProjectDirectoryPath', (Join-Path $source 'src/PeachImage'),
        '--OutputDirectoryPath', $output,
        '--GeneratedAccessModifiers', 'Public',
        '--FileNameFactory', 'Name',
        '--LinksOutputFilePath', (Join-Path $linksFolder 'PeachImage.links'),
        '--LinksBaseUrl', '/api/PeachImage/')
}

# A package's build also rebuilds the packages it references, and a rebuild regenerates their pages, so the link fixing waits until
# every package is built. The pages are generated with links to .md files; the fixing points them at the .html pages the site serves.
Invoke-PackageReference 'PeachDrawing.Text'
Invoke-PackageReference 'PeachDrawing.Core'
Invoke-PeachImageReference
Invoke-PackageReference 'PeachDrawing'
Invoke-PackageReference 'PeachPDF'

& $fixLinks -ApiDocsPath (Join-Path $apiRoot 'PeachDrawing.Text')
& $fixLinks -ApiDocsPath (Join-Path $apiRoot 'PeachDrawing.Core')
# A few of PeachImage's public members are documented with references to its internal types, which have no page to link to.
& $fixLinks -ApiDocsPath (Join-Path $apiRoot 'PeachImage') -UnlinkLearnPrefix 'PeachImage'
& $fixLinks -ApiDocsPath (Join-Path $apiRoot 'PeachDrawing')
& $fixLinks -ApiDocsPath $apiRoot

if (-not $SkipLinkCheck) { & (Join-Path $PSScriptRoot 'Test-ApiDocLinks.ps1') -ApiDocsPath $apiRoot }
