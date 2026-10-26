# A running() element in a margin box now honours the box's vertical-align (#1934)

`content: element()` content was laid out at the top of the margin-box rect, while string content was centred
(css-page-3 §6.2: `vertical-align` acts as in a table cell, UA default `middle`).

Fix, in `HtmlContainerInt.LayoutMarginBoxes`: lay the running box out top-aligned to learn its outer height,
then, for `middle`/`bottom`, lay it out again in the rect shifted down by half/all of the free space. Content
taller than the box has no free space and stays at the top.

Why a second layout rather than `CssBox.OffsetTop`: `OffsetTop` notifies the fragment emitter
(`NotifyGeometryChanged`), and the running box is a detached, shared box whose geometry is not part of any
frozen slot; re-laying out is the same per-page pattern `RunningElementLayout` already uses.

Evidence: `MarginBoxElementVerticalAlignTests` (top/middle/bottom positions on the fragment tree); full
`PeachBrackets.Engine.Tests` suite passes.
