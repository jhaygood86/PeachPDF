# A page margin box's `border` is neither painted nor charged space

css-page-3 §5.1 makes an `@page` margin box a block-level box that accepts the whole box model, and
§5.3.2/§5.3.3's fixed-dimension equality names `border-top-width`/`border-bottom-width` explicitly.
`MarginBoxRenderer` resolves `width`/`height` and `margin`/`padding`, but nothing reads `border-*`:
no border is drawn, and `BoxModelExtent` does not include one in the extent it returns.

Left out deliberately when margin/padding landed. The two halves are separable, and doing only the
second is worse than doing neither — content would move to make room for a decoration the reader
never sees. Doing the first needs `border-*-width` keyword resolution (`thin`/`medium`/`thick`),
`border-style: none`/`hidden` as zero, and a paint step in both the text path
(`MarginBoxRenderer.Render`) and the `content: element()` path
(`HtmlContainerInt.LayoutMarginBoxes`) — a feature, not a box-model correction.

Tracked as #943, which carries the suggested shape. The reader-facing note is the `margin`/`padding`
row in [docs/html-css-support.md](../../docs/html-css-support.md), which says border is not part of
this without citing the issue.
