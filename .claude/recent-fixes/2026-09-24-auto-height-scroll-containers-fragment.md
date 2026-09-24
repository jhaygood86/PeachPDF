# An auto-height scroll container fragments like any block

Closes #1321.

## What was wrong

`MonolithicContent.IsMonolithic` counted every scroll container (`overflow` other than `visible`/`clip`)
as monolithic. A tall `overflow: hidden` wrapper (the clearfix idiom) therefore fit no fragmentainer and
had its content laid out unbroken since #350, with each page showing one slice. The line straddling each
slice boundary was claimed only by the page its top fell on. That page drew it in its bottom margin,
where the page clip hid it, so one line was lost per page. Whether a line happened to straddle
depended on alignment, which is why the issue's `padding-top` was needed to make it appear.

## The load-bearing idea

The spec never asked for that set. css-break-3 §2 says "UAs **may** consider as monolithic any elements
with overflow set to auto or scroll and any elements with overflow: hidden and a non-auto logical height
(and no specified maximum logical height)". An auto-height scroll container grows with its content, so
it has nothing to clip or scroll in the block axis on paper. `IsMonolithic` now requires
`HasConstrainedBlockSize` as well as `IsScrollContainer`: a non-auto logical height, or a max logical
height other than `none`. It applies that to `auto`/`scroll` as well as `hidden`, since §2 only permits
(never requires) treating those as monolithic, and the issue's table shows `overflow: auto` failing the
same way. A percentage against an indefinite containing block counts as `auto`/`none` (CSS 2.1 §10.5,
§10.7, the same test `CssBox.HasAutoBlockEndHeight` uses). In a vertical writing mode the physical
`width`/`max-width` is checked instead.

The exemption applies only where `BreaksInBlockFlow` holds. The box must be display `block` or
`list-item` as computed (`DerivedStyle.ActualDisplay` blockifies floats only, not abspos boxes, so an abspos box qualifies only when its own `display` is `block`/`list-item`). It must not be floated or
page-floated, its parent must not be a flex or grid container, and every ancestor must be a kind known to
carry a break on (`EveryAncestorCarriesABreak`, an allow-list; see the eighth round below). The first
version exempted every scroll container, and a post-change review found two regressions by rendering
against `main`:

- **An `inline-block`** straddling a boundary then had its content fragmented inside its line, which
  PeachPDF cannot do. The box collapsed to a zero-height clip and drew none of its content. §2 separately
  lets inline-level independent formatting contexts stay monolithic.
- **A flex or grid item's `Height`** is set transiently by its engine while measuring, and pinned for
  good by `ItemContentCommit.CommitLayout`. A block-size test on the item therefore answered "auto" to
  `LineRelocation`'s mover before the commit, and "capped" to `LayoutContents` during it. The mover
  didn't move the line, and the commit then laid the content out unbroken, so one line was lost where
  `main` moved the whole flex line.

Both are pinned by `StraddlingAutoHeightScrollContainerInlineBlock_KeepsItsContent` and
`StraddlingAutoHeightScrollContainerItem_MovesWholeWithItsLine`, which fail without the scope limit. The
grid fixture needs `grid-template-columns:60pt`: a grid item stretches past its own `width`, so without
that it is one line tall and never straddles. A tall auto-height flex/grid item loses its boundary lines
on `main` as well; that isn't changed here.

A second review round tightened the block-size test itself:

- **Percentages resolve against layout's own base.** `CssLayoutEngine.PercentageHeightResolves` is
  extracted from `HasDefiniteHeight` and reused. The first version asked
  `IsHeightDefinite(box.ContainingBlock)`. That was wrong for an absolutely positioned box, whose base is
  its nearest positioned ancestor, and for a fixed one, whose base is the page. An abspos `height: 50%`
  under a `position: relative; height: 500pt` ancestor, with an auto-height wrapper in between, was
  wrongly fragmentable.
- **A cap is a length, never a keyword.** The test is `CssValueParser.IsValidLength`, the gate every
  layout site applies to `height`/`max-height`, instead of comparing against the literal `auto`/`none`.
  `max-height: auto` did not actually reach the old comparison as `"auto"` (its test passes either way),
  so this is hardening rather than a live fix.
- **A percentage inside a flex/grid item is treated as a cap.** The item's own `Height` and
  `AlgorithmicDefiniteHeight` change during its engine's pass, so whether a descendant's percentage
  resolves flipped within the pass. That is the same instability that excludes the items themselves.
  `PercentageInsideAFlexOrGridItem_CapsTheBlockSize` resets the item to its unpinned state after layout
  and fails on the earlier code.

A third round made "capped" mean what layout actually caps:

- **`max-height` is tested against its own base.** `ApplyHeight`'s clamp applies a percentage
  `max-height` only when `IsHeightDefinite(box.ContainingBlock)` holds, for abspos boxes too, whereas
  `height` resolves against `PercentageBase`. The previous round sent both through
  `PercentageHeightResolves`. So an abspos `max-height: 50%` under an auto-height in-flow parent counted
  as a cap that layout never applied, and the box was sliced and lost lines.
- **A size fixed without `height` caps too.** A declared `aspect-ratio` (CSS Box Sizing 4 §5) and an
  abspos box with both block-axis insets (CSS 2.1 §10.6.4) give an auto height a size that doesn't grow
  with content. Both are asked of the declaration, not of `TryGetAspectRatioHeight`, which needs the
  used width and so would answer differently before width layout. An `aspect-ratio` card straddling a
  boundary now moves whole again (`EveryScrollContainerValue_MovesWhole`).

A fourth round excluded **floats**. A float computes to `display: block`, but it is placed through
`LayoutContentAtItsAssignedPosition`, which drops the pending break token
([the float-pagination gap](../accepted-gaps/a-floats-own-content-taller-than-one-page-overflows.md),
#1201). Made fragmentable, a straddling `overflow: hidden` float lost every line after the boundary
(F4–F12 of 12). On `main` all 12 were placed, with the boundary line drawn past the band. The first
review's claim that floats "got better" came from a fixture that didn't reach this path. Checking the
same fixture with `overflow: visible` showed that any float crossing a boundary loses its later lines,
not only one taller than a page, and the gap file now says so.

A fifth round extended both exclusions to **descendants**. An auto-height clearfix block inside an
`inline-block` or a float is laid out through that ancestor's assigned position too. Made fragmentable,
it lost every line after the boundary: with 5 lines, F1–F3 were placed and F4–F5 appeared on no page.
The parent-only test had missed it. The walk covers every ancestor (now `EveryAncestorCarriesABreak`), and
`AutoHeightScrollContainerInsideAnInlineBlockOrFloat_PlacesEveryLine` pins both shapes. A clearfix block
inside a flex item needs no such walk: it splits and keeps every line.

A sixth round added **page floats** (`float: top/bottom/top-bottom/snap`, `CssBox.IsPageFloated`), which
`IsFloated` does not cover, to both float checks through `IsFloat`. No fixture showed a difference from
`main`: a straddling 5-line page float is moved to a page edge and never breaks. A 12-line
`float: bottom` loses lines (only up to F5 placed) on `main` and on this branch alike, a pre-existing
defect, now tracked as #1332 ([gap](../accepted-gaps/a-page-float-taller-than-a-page-loses-the-lines-that-dont-fit.md)). The exclusion is there by construction: a page float is moved to its edge
whole, like a float, so it cannot continue a break either.

A seventh round added **vertical writing-mode ancestors** to the walk (`IsUnresumableOrthogonalFlow`).
Such a block lays its children out through `LayoutVerticalBlockChildren` →
`LayoutContentAtItsAssignedPosition`, which drops the child's break token, and the vertical parent is not
itself `IsMonolithic`, so it doesn't detach the fragmentainer either. The measured result was worse than
the review predicted: a clearfix `writing-mode: horizontal-tb` block inside a `vertical-rl` parent placed
**no** lines at all, where `main` placed all five. A vertical scroll container on its own was checked
too: it keeps every line on both the branch and `main`.

An eighth round replaced the deny-list with an **allow-list**, `EveryAncestorCarriesABreak`. Every
ancestor must be block, list-item, block-level flex/grid, or a block-level table and its row groups, rows
and cells, and not floated, page-floated or `IsUnresumableOrthogonalFlow`. The review named two more
break-dropping ancestors, a table caption (`LayoutCaptionGroup`) and inline-level flex/grid/table. A
probe over nine ancestor shapes confirmed the caption and `inline-table` (F4–F5 lost; `main` placed
all five). `inline-flex`/`inline-grid` placed every line, because their items are measured with
breaking off. Each earlier round had found the next deny-list gap only by measuring lines disappear. The
allow-list inverts the failure mode: an unlisted ancestor keeps `main`'s monolithic behaviour, which can
leave the fix out but never lose content. `AutoHeightScrollContainerUnderAnyAncestor_PlacesEveryLine`
pins all nine shapes, and the caption and `inline-table` rows fail on the deny-list version.

`IsScrollContainer` itself is unchanged, since only `IsMonolithic` calls it. Table cells are unaffected
either way: `LayoutContents` already excluded them from suppression by display.

## What was found by running it

- The 14 failing tests after the change were all fixtures that used a bare `overflow: hidden` card as
  the stock monolithic box. They now add `max-height: 1000pt` (or `10000pt` for the tall ones), which
  caps the block size without changing any geometry, so each still pins what it pinned.
- The new `TallAutoHeightScrollContainer_DrawsEveryLineInsideAPageBand` reproduces the issue exactly
  against the old classifier (`L9`/`L19`/`L28` end 2–11pt past the 180pt band) and passes with the fix.
- Given `max-height: 10000pt`, the same fixture still loses those three lines. That is the capped case,
  left as an [accepted gap](../accepted-gaps/a-capped-scroll-container-taller-than-a-page-loses-its-boundary-lines.md)
  tracked by #1328.

## User-visible side effect

An auto-height `overflow: hidden|auto|scroll` card that straddles a page boundary is now split across it
instead of moving whole to the next page (see the migration note). That is what browsers do when
printing. Authors who want the old result can add `break-inside: avoid` or a `max-height`.
