# The API reference links across packages, and covers PeachImage

**What:** `.github/scripts/Build-ApiReference.ps1` now generates the whole reference (`pages.yml` and the new `api-reference.yml` PR
check both call it) and `Test-ApiDocLinks.ps1` fails it on a link that does not resolve. Each package writes a DefaultDocumentation
links file to `artifacts/api-links/` and reads the others' (`src/ApiReferenceLinks.props`, imported by `ApiReference.props` and by
`PeachPDF.csproj`), so a `PeachDrawing.Text.Typeface` in a Core signature links to the Text page instead of a learn.microsoft.com
URL that does not exist. PeachImage (a separate repository) is documented from the tag `PeachPDF.csproj`'s `PackageReference` names,
cloned into `artifacts/`, by running DefaultDocumentation's console tool over the built assembly; it has a guide (`docs/peachimage.md`)
and its own `/api/PeachImage/` nav entry.

**Traps found by running it:**
- Build order is the reference order (Text, Core, PeachImage, PeachDrawing, PeachPDF), since an extern links file must exist when its
  consumer builds. But **building a package also rebuilds the packages it references through `ProjectReference`, and
  `-p:DisableDefaultDocumentation=false` is global, so every downstream build regenerates the upstream pages** and undoes any link
  fixing already done. First attempt fixed links per package as it went and left ~8500 `.md` links; the fixing now runs once, after
  every build.
- A links file names pages `X.md`; the site serves `X.html`. `Fix-ApiDocLinks.ps1` rewrites any root-relative `/api/...md` link, and
  the checker fails on a `.md` destination, because a link to the source is a link to nothing on the site.
- The checker's link regex must exclude `(` from the destination's leading run: signature-named pages are
  `Foo.Bar(string,double).md`, and a greedy `[^\s')]+` stops at the first `)` and "finds" a truncated, nonexistent target.
- **DefaultDocumentation generates no page for the properties of a positional record** (`Size.Width`, `GradientStop.Position`,
  `FontPalette.Overrides`, ...), nor for anything not public, so a `<see cref>` to one becomes a learn.microsoft.com link that 404s.
  The fix is in the source: `<c>Width</c>` rather than `<see cref="Width"/>`. The checker reports any learn.microsoft.com link for a
  PeachPDF/PeachDrawing/PeachImage name for exactly this reason. PeachImage's own few such references (to its internal types) cannot be
  fixed here, so its pages alone are unlinked to plain text (`Fix-ApiDocLinks.ps1 -UnlinkLearnPrefix PeachImage`).
- PeachImage pins an SDK band in its own `global.json` and builds with warnings as errors; the script deletes the former and passes
  `-p:TreatWarningsAsErrors=false` for the latter. Its project does not reference DefaultDocumentation, so the tool is taken from the
  NuGet cache the other packages' restore already filled.

**Not done:** the properties of positional records still have no reference page at all (a DefaultDocumentation limit, not fixed by
documenting them); Windows PowerShell 5.1 and pwsh both run the scripts, but only 5.1 was exercised locally.

**Evidence:** all five references generate (Text 657 pages, Core 533, PeachImage 255, PeachDrawing 138, PeachPDF 327 at the root) and
`Test-ApiDocLinks.ps1` reports 12,805 links in 1,910 pages resolving, with zero learn.microsoft.com links to our own types.
