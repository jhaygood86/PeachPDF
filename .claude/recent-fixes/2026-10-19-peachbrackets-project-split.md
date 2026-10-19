# The HTML/CSS/SVG/MathML core moves into its own project, `PeachBrackets`

Phase 3a of the library extraction: a mechanical move with the seams cut, not yet a public API. `CSS/`, `Html/`, `Svg/`, `MathML/`, `Utilities/`, `css-properties.json` and the
source generator reference now belong to `src/PeachBrackets/`; `PeachPDF` references it. Nothing is renamed and nothing new is public, so the suite is the proof: `PeachPDF.Tests`
(17444), the CLI, source-generator and PDF-writer suites pass, and the rebuild is warning-free.

- **Namespaces are unchanged on purpose.** Renaming ~1,100 files to `PeachBrackets.*` and designing the public document-level API are separate, reviewable steps; doing them in the
  move would make the diff unreadable. The temporary `InternalsVisibleTo` (`PeachPDF`, `PeachPDF.Tests`, `PeachPDF.Cli`) is the same kind of bridge the PDF writer had, and goes the
  same way: replaced by public API, not grown.
- **Seams cut (the core had five dependencies on PeachPDF's root namespace):** the colour-scheme enum (`ColorSchemePreference`, mapped from `PdfColorScheme` in `PdfGenerator`), the clip
  report (`ClipLog`/`ClippedTextEntry`, converted to the public `ClipReport` after layout), the declarative stylesheet (the core takes the parsed `CssData`; `PeachPdfCssContent`
  stays a thin public wrapper), the image-callback size (the core takes `PeachDrawing.Core.Size`; `ContainerBuilder` adapts the public `PdfSize` callbacks), and the `Set(box, name, PdfLength|PdfColor)`
  overloads (now extension methods in `PeachPDF.Layout`, since the value types are public PeachPDF API).
- **`HtmlRenderException`/`HtmlRenderErrorType` are the exception to "public types stay in PeachPDF".** The core throws them from deep inside layout, paint and loading, and callers
  catch the exact type, so wrapping at the boundary would change behaviour. They moved with the core (same namespace) and PeachPDF forwards them with `[TypeForwardedTo]`. The public-surface
  test therefore also counts forwarded types, or the forward would read as a removal.
- **Traps found by running it.** `Network/` looked like core (the files are loaders and a MIME table) but is public PeachPDF API that the core never touches; moving it deleted 26 lines
  from `PublicApi.txt`, which the snapshot caught. The 189 `CS1574` warnings after the move were all XML `cref`s to PeachPDF types the core can no longer see, rewritten to `<c>` by
  script. MimeKit stays a PeachBrackets dependency: the HTML parser reads MHTML with it directly.
