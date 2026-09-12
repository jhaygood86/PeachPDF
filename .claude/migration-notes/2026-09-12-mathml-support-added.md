# `<math>` now renders as MathML instead of falling through as plain (invalid) HTML

Before this change, PeachPDF had no special handling for `<math>`/MathML child elements at all —
`CssBox.CreateBox` had no case for `HtmlConstants.Math`, so a `<math>` element (and its `<mi>`/`<mn>`/
`<mfrac>`/etc. children) became ordinary generic `CssBox` instances, laid out and painted as if they were
unknown/invalid HTML: each MathML element's own text content would render as plain inline text in
document order, with no fraction bars, radicals, scripts, or any other mathematical typesetting -
effectively a garbled, unstyled rendering of whatever text nodes the formula happened to contain (e.g.
`<mfrac><mn>1</mn><mn>2</mn></mfrac>` would have rendered as the plain text "12" with no fraction bar or
stacking, since `<mn>` carries no CSS `display` of its own and neither element received any tag-specific
layout treatment).

`<math>` now renders as real vector-PDF-rendered MathML (see
[docs/architecture.md's MathML Rendering section](../../docs/architecture.md#mathml-rendering) and
[Supported MathML Features](../../docs/supported-mathml-features.md)) - a document that happened to
contain `<math>` markup before (whether intentionally authored MathML that was silently broken, or
`<math>` used incidentally as a non-semantic wrapper element) will now render that content as an actual
typeset formula instead of the previous garbled plain-text fallback.

Additionally, when [tagged PDF output](../../docs/html-css-support.md#tagged-pdf-pdfua-support) is
enabled, `<math>` now defaults to the `Formula` structure type (previously it had no default mapping and
fell through to the generic `Div`/`Span` `auto` fallback like any other unrecognized tag), and carries its
original MathML source as a PDF 2.0 Associated File - see
[MathML Associated Files](../../docs/html-css-support.md#mathml-associated-files).

A new `PdfGenerateConfig.PdfVersion` option (default `Pdf17`, unchanged from prior behavior) was also
added - `Pdf20` is a new, explicit opt-in and changes nothing for a caller who doesn't set it.
