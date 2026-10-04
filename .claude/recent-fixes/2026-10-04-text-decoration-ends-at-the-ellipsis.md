# A text decoration on a `text-overflow: ellipsis` line ends where the kept text does (#1625)

## Load-bearing idea

Truncation is decided at word-paint time (`PaintLineWithEllipsis`), but a decoration is painted with
its *box*, **before** the descendants whose words may hold the cut: `<a style="text-decoration:
underline"><span>long text</span></a>` draws the `<a>`'s underline, then descends into the `<span>`
that gets truncated. A block's propagated decoration (`PaintPropagatedDecoration`) is the same - it is
drawn at the block, ahead of the anonymous child holding the words. So the decoration cannot read the
cut back from what the word painter recorded (`_linesAlreadyTruncated` is filled too late).

Instead the decision was split out: `PlanLineTruncation` is the pure "where does this box's slice of
the line get cut" half of the old `PaintLineWithEllipsis`, and `PaintLineWithEllipsis` is now plan +
paint. `EllipsisCutOf(g, line)` re-asks that same planner, per line, over every box that contributes
words to the line **in tree order** (index of the page's fragment tree, built lazily on first ask, so a
page with no ellipsis never walks it), first cut wins - the same rule `_linesAlreadyTruncated` gives
the painter. `PaintDecoration` takes the resulting `EllipsisCut` and clamps its inline-axis span to the
anchor (`spanEnd = min(spanEnd, anchor)` LTR, `spanStart = max(...)` RTL, same for the Y axis in a true
vertical mode). The anchor is the end of everything kept, so the ellipsis itself is not decorated -
matching Chrome.

## Traps

- It relies on paint order == fragment-tree order for inline content. That is the same assumption
  `_linesAlreadyTruncated` already makes; a stacking-context reordering of text would break both.
- `FragmentPaintHarness.PaintBox` goes through `PaintFragment`, never `Paint`, so `_pageRoot` is null
  there and `EllipsisCutOf` returns null (decoration untouched). Tests for this must use `PaintPage`.
- `BoxFragment` is a record: `List.Contains` compares structurally and deeply. The index dedups by
  `ReferenceEquals`.
- Do **not** gate the lookup on "the decorating box or its containing block is the truncating
  block": an ancestor block's propagated decoration descends into the truncating block (review
  finding). The index only holds fragments whose own containing block truncates, so asking for every
  decorated line is cheap.
- The index skips `display: none` / non-visible boxes, mirroring `PaintFragment`'s early-out, so a
  box that never paints never claims a line's cut.
- A drop of a box's first word (`i == 0`) used to anchor the ellipsis at the block's content-start
  edge, wrong when an earlier sibling box was kept; it now anchors at the later of that edge and the
  dropped word's own start. The decoration inherits the anchor, so this would otherwise have erased
  the kept sibling's underline.
- Known approximation: the clamp side comes from the truncating block's direction, so a mixed-direction
  (bidi) line is as approximate for the decoration as the existing plan is for the ellipsis.

## Evidence

`TextOverflowDecorationTests` covers block, inline, ancestor-of-the-cutting-box and ancestor-block
decorations, line-through/overline/double, RTL, a vertical column, a fitting line (compared against
identical markup without `text-overflow`), no-ellipsis, a decorated box before/after the cut, a dropped
first word after a kept decorated sibling, nothing kept, and `text-indent`. Without the change the
block/inline/ancestor/line-through/RTL/double/overline/vertical cases fail. Full `net8.0` suite: 15471
passed, 0 failed. Showcase `text_overflow` section 3b rasterized through PDFium and MuPDF: both end the
line at the kept text. Diff-coverage against HEAD: 99% (102 lines, 1 missed: the vertical arm of the dropped-first-word anchor).

Not mirrored by the index (a box can claim a cut it would not paint): `IsAnyRectVisible`,
`_contextMembers`, `empty-cells: hide`, `_stopAt`. Cuts are monotonic along a line, so a clipped-away
box is always after the real cutter and loses "first wins"; revisit if that stops holding.
