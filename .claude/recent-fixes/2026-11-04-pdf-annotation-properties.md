# `-peachpdf-pdf-annotation-*`: annotations from CSS, including attached-file icons (#2191)

The issue asked for a clickable paperclip that opens a file already in `PdfGenerateConfig.Attachments`. Prince has no such thing (only
document-level attachments and text-note annotations, `-prince-pdf-annotation-*`), so the shipped design is the general family: twelve
properties (`-type`, `-attachment`, `-title`, `-contents`, `-author`, `-icon`, `-color`, `-position`, `-createdate`, `-modifydate`,
`-merge`, `-path`), eleven of them with undocumented `-prince-pdf-annotation-*` aliases (`aliasOf` entries in `css-properties.json`,
like the bookmark aliases; `-attachment` and `-path` have no Prince twin). Kinds: text note, highlight/underline/wavy/line-through,
file-attachment, plus the ones Prince lacks - square, circle, line, free-text, stamp, ink. `-position` places the icon of an icon kind and
the `/Popup` window of every other kind.

## What carries the weight

- **Geometry is read from the fragment tree, not `CssBox.Bounds`.** The first version walked the box tree and read `Bounds`/`Rectangles`
  like the form-field pass; every inline-block came out at the page origin. Fragments are what layout hands to paint
  (`AnnotationPropertyCollector` walks `FragmentainerFragment.Root`; an inline's `Lines`, a block's `Rect`).
- **An empty inline has no fragment at all** - layout notes its thickness and emits nothing, so `<span></span>` (the issue's own example)
  is invisible to the fragment walk. The collector finds such children via `box.Boxes` minus the fragment's `Children` and places them at
  the end of the previous sibling's last word (or the start of the next one, or the parent). It has no width, so the 12pt icon covers
  whatever follows; documenting `display: inline-block` + size as the way to reserve room, rather than faking layout width.
- **The embedded file is stored once.** `EmbeddedFilePlan.Apply` now remembers each `PdfFileSpecification` by name; the annotation's `/FS`
  is that object, so `/AF`, the name tree and the icons all reach one stream (a test checks the object number matches `/AF`).
- **Every kind paints an appearance** (PDF/A). The writer package owns the drawing (`PdfAnnotationAppearance`) so a caller with no HTML gets it
  too; PeachPDF's `AnnotationBuilder` only translates. An appearance form of zero width/height throws ("PdfForm is too small"), so a
  mark/shape on a zero-area box is skipped in the collector; only icons may stand on no area.

## Traps

- **`Converters.cs` static fields initialise in textual order.** `PdfAnnotationColorConverter = ColorConverter.OrAuto()` declared *above*
  `ColorConverter` read null and threw a `NullReferenceException` in `OrValueConverter.Convert` only once something validated the property
  (the `ComputedStyleTests` defaulting loop). It must sit below `ColorConverter`.
- Test fixtures: the default page margin is 10pt, so a box at `margin-left: 50pt` starts at x=60.
- A `PdfAnnotation` always writes `/F Print`, `/NM` and `/M` (`PdfAnnotation.Initialize`); `/DA` for FreeText must be written as a Latin-1
  string (`SetAsciiString`) - the Unicode path prefixes a BOM and readers do not parse it as an appearance string.

## Evidence

`PdfAnnotationPropertyIntegrationTests` (27, saved bytes read back with the inspector), `PdfAnnotationApiTests` (25, writer alone, no
PeachPDF reference), the CSS-OM baseline regenerated. The harness showcase `pdf_annotations` (PDF/A-3B) rasterized through MuPDF and
PDFium: every icon, mark, shape, stamp and ink path draws identically in both.

## Not done

Annotations in page margin boxes; annotation arcs in ink paths are chords; `-position` percentages are of the element box only.
