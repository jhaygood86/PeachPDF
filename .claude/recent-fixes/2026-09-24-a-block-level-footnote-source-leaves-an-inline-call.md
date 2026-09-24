# A block-level `float: footnote` source is replaced by an inline call (#753), and #750's shapes are pinned

## #753: any source that generates a box

`DomParser.DetachFootnoteBodies` only detached a source that was `IsInline`, so `<div style="float:footnote">` was
left in place and behaved as `float: none`. css-gcpm-3 §2.2 replaces the element with a `::footnote-call`
whatever its own `display` was, and the call is inline by default (the UA sheet in §2.6 sets no `display`, so the
initial `inline` applies). `IsFootnoteSource` now accepts any source that renders: not `display: none`, not
absolutely positioned or `position: running()` (CSS 2.1 §9.7 computes `float` to `none` there), and not a
table-internal display, which cannot hold an inline call.

**Placement is the only real work.** A container holds block-level or inline boxes, never both (CSS 2.1
§9.2.1.1), and the call is inline. `LocateCall` puts it: at the source's own index for an inline source, a flex
or grid container, or a container with no other block-level in-flow child; otherwise into the anonymous block the
correction passes already made for the run beside it, merging the run before and after the source into one when
both exist (the source counted as block-level, so the run around it had been split in two), or into a new
`IsInlineRunWrapper` block. Detaching *after* the correction passes is what makes this a local decision; doing
it earlier would have meant teaching `JoinsTheInlineRun`, `ContainsVariantBoxes` and the two
`Correct...InlineBlockFormattingContexts` arms a new float-like kind, which #1038 showed have to agree.

## #750: probed, mostly already fine

Written as tests first, as the issue asked: a footnote inside a table cell, a flex item and a grid item reserves a
note area on its page and reaches the fragment tree's note area; a footnote that is a direct child of a flex
container becomes the call in its place (the call is the item). The one that does not work is a repeated
`<thead>`/`<tfoot>`, whose single laid-out subtree is translated per page, so the call has no page of its own
(`CssProxyBox`); it is declined and left as ordinary content, recorded in
[the remaining gap](../accepted-gaps/footnote-inside-table-cell-flex-grid-item.md).

**A comment was wrong and is fixed:** `NormalizeFlexOrGridItem` said `Floating.Footnote` is not coerced because
"IsExcludedFromFlow already says" it is not an item. `IsFloated` does not include `Footnote`, so it does not say
that; the real reason it is left alone is that the source is replaced by its call before layout.

Evidence: 10 new/rewritten tests in `FootnoteIntegrationTests` (between blocks, only child, beside an inline run
with exactly one merged wrapper, the sources that stay ordinary, a body with block children, table cell/flex/grid
item, direct flex child, repeated header); full net8.0 suite green.
