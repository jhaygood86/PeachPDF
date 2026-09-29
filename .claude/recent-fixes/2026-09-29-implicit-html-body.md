# Implicit html/body (#1370)

`HtmlParser.EnsureHtmlAndBody` runs after `ParseDocument` in `DomParser.GenerateCssTree` only. It must NOT run for `GenerateFragmentCssTree` (fragments are spliced into an existing tree) nor inside the `<noscript>` recursion. Head-content elements leading the document are left as direct children of `html`, no `head` box is synthesised.

Fixtures that relied on a body-less document having no 8px margin (SvgBackdrop, FootnoteColumnConvergence, DisplayNoneAmongInlineContent) now set `body{margin:0}`. `AnonymousBoxDefaultingTests.AListItem...` fails on baseline too (2.60x vs 2.55x bound) — unrelated.
