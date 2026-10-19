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
- Not done yet: `FormFieldBuilder` and `StructureTagBuilder` still read `CssBox` and write the PDF document directly. They need a
  public element handle (tag, attributes, text) before they can move out of the HTML core.
- Evidence: full `PeachPDF.Tests` net8.0 run unchanged (18249 passed), including the link pagination, transform, running-element and
  bookmark outline suites.
