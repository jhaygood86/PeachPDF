# Hyperlinks and the outline are collected format-neutrally, then written as PDF

Part of splitting the HTML core (PeachBrackets) from the PDF writer. `DocumentAnnotationCollector` (`Html/Core/Annotations/`) now
works out the links and bookmarks of a laid-out document - page, rectangle, quadrilateral, target - and `PdfGenerator.HandleLinks` /
`BookmarkOutlineBuilder` only turn them into PDF annotations and outline entries.

- **Convention of the model (`DocumentAnnotations`):** points, measured from the page's top-left corner with y running down. The
  PDF y flip (`page.Height - (y + h)`) happens once, in the PDF consumer. A destination's `Top` is flipped there against the *target*
  page's height, not the link's page.
- **`/QuadPoints` is a PDF 1.6 key**, so the collector always reports the quad and the consumer drops it for `document.Version < 16`.
- **`AnnotationPlacement.TryMap` has a `yUp` flag**: form widgets still need y-up geometry and the appearance matrix (`LinearPart`);
  links use `TryPlaceLink`, top-down. `PageFrame.ToPdf` is `ToPage` plus the flip, built from the same expression so untransformed
  results are bit-identical.
- **Form controls follow the same split.** `DocumentAnnotations.FormFields` holds `PageFormField`s (page, bounds, upright content size,
  affine appearance transform, `FormFieldClassification`, and a `FormFieldAppearance` that paints each state onto any `Canvas`).
  `FormFieldAppearance` is the old `FormFieldAppearanceBuilder` minus its PDF form creation; `FormFieldBuilder` now only makes the AcroForm
  objects and appearance streams (`CreateForm` + `PdfCanvas`). The appearance transform is y-down in the model; the PDF consumer mirrors
  it (`m12`, `m21` negated). `HtmlContainerInt.FormFieldBuilder` became the `InteractiveForms` flag - the painter only ever tested it for null.
- **Tags use an opaque `StructureNode`.** `StructureTagBuilder` only ever used a `CssBox` as an identity key (plus one `StructureTagMapper.Classify`
  call), so `IStructureTagSink` now takes `StructureNode` (reference identity, implicit from `CssBox`) and the painter classifies
  `display: contents` ancestors itself, passing `PhantomAncestor(node, structureType)`. `PageLink.Source` is a `StructureNode?`.
  `PdfFeatures/` (`StructureTagBuilder`, `FormFieldBuilder`, `BookmarkOutlineBuilder`) no longer mention `CssBox` at all.
- **`Layout/` stays with PeachPDF.** `PageDescriptorBuilder`'s use of `PageSizeConverter` is not a seam to cut: the declarative builder is part of
  PeachPDF's own public API (it takes `PageSize`, feeds `PdfGenerator`), not of the HTML core.
- Evidence: full `PeachPDF.Tests` net8.0 run unchanged (18249 passed), including the link pagination, transform, running-element and
  bookmark outline suites.
