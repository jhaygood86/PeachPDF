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
- Not done yet: `StructureTagBuilder` still reads `CssBox` and writes the PDF structure tree directly; `FormFieldClassification` is
  still built from `CssBox` by `FormFieldMapper` inside the collector (fine, it runs in the HTML core). Tags need a public element
  handle (tag, attributes, text) before `StructureTagBuilder` can move out.
- Evidence: full `PeachPDF.Tests` net8.0 run unchanged (18249 passed), including the link pagination, transform, running-element and
  bookmark outline suites.
