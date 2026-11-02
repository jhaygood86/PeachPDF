# What veraPDF found in the first PDF/UA files, and what fixed each

Validated with veraPDF 1.30.3 (`verapdf --flavour ua1|ua2`) on a page of header, nav, a figure with an inline SVG and a caption, paragraph with a link, ordered and unordered lists, a table and a footer. The unit tests read the file back (`PdfUARequirementsTests`); veraPDF itself is a manual check, not in CI (it is a Java program).

- **Annotation parent tree**: `/ParentTree` values for an annotation's `/StructParent` must be the structure element, not an array of one (the array is for page content, indexed by MCID). veraPDF reported the link as untagged (7.18.5-1) until this changed.
- **Link `/Contents`** (7.18.5-2, 7.18.1-2): `IStructureTagSink.LinkAdded` got an overload carrying the accessible name from the tree; `PdfTagRecorder.LinkAnnotation` writes it.
- **Grouping Figure alt** (7.3-1): `<figure>` is a `Figure` around the SVG's own `Figure`; the outer one has no picture to describe, so it takes the figure's name (from `figcaption`) through `StructureDetails.AlternativeText`.
- **Untagged underline** (7.1-3 / 8.2.2-1): text decoration lines were drawn between marked-content sequences. They are now an artifact, opened only when the box declares a decoration line - an unconditional empty `BMC`/`EMC` pair broke `WordSeparatorPdfOutputTests`, which counts `EMC` against `BDC`.
- **UA-2**: `/Namespaces` on the root and `/NS` on every element (a missing `/NS` means the PDF 1.7 namespace, which UA-2 rejects); `ListNumbering` on every `L` that has `Lbl` items; no `Span` loose in the document (blocks with only inline content are `P`); structure destinations (`/SD` on the GoTo action, `[element /Fit]` as an outline `/Dest`), which need the target element: `PagePosition.Target` carries it from the collector.

Not done: a destination to an element that has no structure element (an `id` on a `display: none` or untagged box) falls back to the page destination, which UA-2 does not accept for that link.
