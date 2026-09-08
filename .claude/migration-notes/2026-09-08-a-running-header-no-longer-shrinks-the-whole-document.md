# A running header or footer wider than the content box no longer shrinks the whole document

A `position: running()` element is painted into a page margin box, so its width is bounded by the
page margin band rather than by the flow's content box — a header spanning the full page width is
wider than the content box by design, and legitimately so. It was nonetheless growing the document's
own recorded extent (`HtmlContainerInt.ActualSize`), which reports the document as overflowing its
page when nothing in the flow does.

With `ShrinkToFit` enabled, that made the whole document scale down to fit content that was never in
the flow. Measured: a Letter invoice with a company header and footer band rendered at 0.968 scale,
while the same document with both bands removed did not scale at all.

A running element and its descendants no longer contribute to `ActualSize`. Ordinary content —
including content that is genuinely too wide for the page — still does, so `ShrinkToFit` behaves
exactly as before for everything that is really in the flow.

See [Running elements](../../docs/html-css-support.md#running-elements-position-running--element) in
`docs/html-css-support.md`.
