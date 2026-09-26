# The font and text engine is its own assembly and NuGet package

`src/PeachPDF/Fonts` and `src/PeachPDF/Text` moved (with `git mv`, so history follows) to `src/PeachDrawing.Text`, published
as `PeachDrawing.Text` at the same version as PeachPDF, which now depends on it. This is a move, not a redesign: the
steps before it ([infrastructure](2026-09-25-font-engine-infrastructure-decoupled-from-pdfsharpcore.md), [face-style
types](2026-09-25-engine-owns-its-face-style-types.md), [`Typeface`](2026-09-25-the-engine-hands-out-a-typeface.md)) had already made the
engine independent of everything else in PeachPDF, so it compiled as its own project on the first try.

**Layout.** `Internal/Fonts/**` and `Internal/Text/**` (incl. the embedded Unicode/hyphenation resources, which are read back by
name suffix through `typeof(X).Assembly`, so they survived the move untouched). Namespaces are `PeachDrawing.Text.Internal.Fonts`
and `PeachDrawing.Text.Internal.Text`; the public API, when it exists, gets public namespaces of its own.

**The bridge.** Nothing is public yet. PeachPDF and PeachPDF.Tests read the engine's internals through a temporary
`InternalsVisibleTo`, so the whole suite passes unchanged while the API is designed; the compiler will list exactly what PeachPDF
touches once the bridge is being removed. Until then the package contains the engine with no stable public surface, and its README
says so.

**One version.** `src/Version.props` holds `<PackageVersion>` and both csprojs import it; `publish.yml`, `pages.yml` and the demo
build read it from there. `publish.yml` builds/packs both projects and pushes `PeachDrawing.Text` **first**, since PeachPDF
depends on it at the same version. An owner action outside the repo is needed before the first release: the `PeachDrawing.Text`
package id and the trusted-publishing policy for it on nuget.org.

**Notices.** The package ships `LICENSE`, `README.md` and its own `THIRD-PARTY-LICENSES.md` (PDFsharp-derived font readers,
the three HarfBuzz ports, the UCD tables, the hyphenation patterns; split out of the root file, with the PDFsharp MIT text and
`Internal/Fonts/OpenType/LICENSE.md` beside the derived code). The root file keeps PdfSharpCore, ExCSS and the bundled test fonts
and points at the engine's. The PeachPDF package previously carried **no** licence file despite containing PDFsharp and ExCSS; it
now packs `LICENSE` and the root notices. The `peachpdf` CLI embeds and prints both notices files, and the release archives ship both.

**Traps.** (1) The relative-namespace trap: a reference written `Fonts.OpenType.MathTable` inside a `PeachPDF.*` namespace resolved
by relative lookup and silently stopped resolving after the rename; a scan for partially qualified `Fonts.`/`Text.` references found
two code uses and a few doc crefs. (2) crefs from the engine to PeachPDF types (`PdfSharpAdapter`, `CssBox.CharScripts`, ...) are
warnings once the assembly is separate; they became plain `<c>` text. (3) `PackageVersion` was read with `grep`/`XmlPeek` from
`PeachPDF.csproj` in three places, all moved to `Version.props`.
