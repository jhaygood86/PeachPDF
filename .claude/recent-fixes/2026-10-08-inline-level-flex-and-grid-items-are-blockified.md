# Inline-level flex/grid items are blockified (issue #1003)

`DomParser.NormalizeFlexOrGridItem` now applies css-display-3 §2.7 to every in-flow item:
`inline`/`inline-block` → `block`, `inline-flex` → `flex`, `inline-grid` → `grid`,
`inline-table` → `table`. Closes the accepted gap recorded for issue #1003 (its file is deleted with
this change).

**The load-bearing idea: the blocker named in the issue was not the blocker.** The issue said
blockifying needs CSS 2.1 §10.3.4 intrinsic width for block-level replaced content in `GetBoxWidth`
first. It does not. Blockifying alone regressed six tests, but the cause was
`DomParser.CorrectReplacedElementBoxes`: it sees a `display:block` `<img>`/`<svg>` and wraps it in a
synthetic `IsReplacedBlockWrapper` block (demoting the image back to inline so its phantom word sizes
it). Inside a flex/grid container that wrapper became the *item* and the image a bare inline inside it,
so centring, stretching and the page-break relocation all acted on the wrapper. The fix is one
condition: skip the wrapper for a direct child of a flex/grid container, leaving the image as the item
itself — the exact shape `PerformLayoutBlockified` already measured it in (it flips an inline item to
`block` for the duration of its layout).

**Found by running it, not reading it.** The wrapper (added for #1137/#1179/#1182 after the issue was
filed) was the missing piece; the issue's text predates it. Applying the arms alone and running
`--filter "Flex|Grid|PixelsPerInch"` named the six failures; skipping the wrapper took them to 0.

**Not done.** §10.3.4 in `GetBoxWidth` is still unimplemented for a block-level replaced box that is
*not* a flex/grid item and not wrapped; nothing here needed it. Pre-existing, unchanged by this fix
(identical on the merge base): a replaced flex row item measures ~38.8pt tall for a 36pt image, and a
replaced grid item stretches to the column width.

**It is not layout-neutral, and the green suite did not say so.** A geometry probe (a dump of every
box's Location/size over ~25 flex/grid shapes, run on the merge base and on the change, then diffed)
found that a *nested* `inline-flex`/`inline-grid`/`inline-table` item used to reach layout as an
inline-level box, so its own children were laid out at (0,0) with no size: `gap`/`align-items`, grid
tracks and table cells were silently ignored (an `inline-table`'s cells did not paint). `PerformLayout
Blockified` only flipped the item's own `IsInline` display for the measure, not the container
semantics its children needed. Blockifying fixes it; `Nested*Item_*` tests pin it and fail on the merge
base. Replaced items other than `<img>`/`<svg>` (iframe/video/object), form controls, `<br>` between
items, floated / `display:contents` / absolute images were all identical before and after, and are
pinned or probed as such.

**Two more shapes changed, both for the better** (found by a second probe after review): an absolute or
fixed `<img>` directly in a flex container used to get an in-flow `IsReplacedBlockWrapper` that then
took part in the flex line, so a fixed image pushed its sibling 72pt along and made the container as
tall as the image; the wrapper skip now applies to every direct child of a flex/grid container, so an
out-of-flow image is a bare out-of-flow box. An `inline` item holding a block (`<span>pre<div/>post
</span>` as an item) used to lay its rows over one another (and clip off the page); it now stacks.
Both are pinned by tests that fail on the merge base. The skip is deliberately *not* limited to in-flow
items — limiting it kept the old wrapper, and with it the bug.

**A text run must not be normalized.** `FlattenDisplayContents` re-runs `NormalizeFlexOrGridItem` on
every lifted child, and the cascade never visits a text box, so the new inline arm turned a lifted text
run into `block`. Rendering happened to be identical, which is why only a direct assertion on the box
caught it; `IsAnonymousTextRun` now returns early.

**Showcase diff.** 213 showcases generated in Release before and after: 3 differ at content-stream
level (`invoice`, `print_catalog`, `stretch_sizing`), none at pixel level in PDFium or MuPDF. (The Debug
build of the TestHarness trips an assertion partway through the corpus on both; use Release.)

**Still wrong, not touched.** A nested `inline-grid` item's auto width in a flex row (290pt for a 100pt
grid). A review-pass observation, not caused by this change: wrong before too.

**`IsFlexOrGridItem` must stay in `CssBox.StartsNewLine`.** It looked redundant once the cascade
blockifies the item: removing it left the whole suite *and* a geometry probe green. It was not — the
cascade never visits an anonymous text run, so `AB CD<span style=position:absolute>x</span>EF GH` in a
flex column (two text runs) went back to being summed onto one line, 60.24pt against the 30.94pt a
column of two blocks gives. Only a probe shaped like that found it (a review did), and
`TextRunsSplitByAnOutOfFlowChild_InAColumn_MeasureAsTwoLines` now pins it. The *other* display-oracle
use, in `IsAtomicInlineRequiringIsolatedMeasurement`, is gone: it only ever mattered for an item whose
own display was an atomic inline value, and no element item has one now. The same goes for the
temporary display swap in `PerformLayoutBlockified` (flex and grid): it now fires only for a text run,
and its remarks say so. **Lesson: a green suite plus a probe is evidence for the shapes in the probe.**

**Shared table.** The inline-to-block mapping lives in `DomParser.BlockifyInlineLevel`, used by both
`BlockifyPositionedBox` and `NormalizeFlexOrGridItem`.

**A replaced grid item stretched across its track — a spec deviation (css-grid-2 §6.2, issue #1662)
that this change would have turned into a regression, so it is fixed here.** A default (inline) `<img>`
in a grid was already stretched on the merge base. But an `<img style="display:block">` — what CSS
resets such as Tailwind's preflight set on every image — was held in an `IsReplacedBlockWrapper`, which
kept its natural size; skipping the wrapper for flex/grid items put it on the stretching path (36pt
tall to 300x152pt in a one-column grid). The fix: `ResolveSelfAlignment` no longer collapses `normal`
into `stretch`, and an `<img>`/inline `<svg>` under `normal` keeps its natural size (an explicit
`stretch` still stretches it). **Trap:** that alone changed nothing — `MeasureItemHeight` pins the item's
width to its track to measure a row, which leaves the image's phantom word measured at the track width
for the pass that places it (the trace showed `GetFitContentWidth` returning 100pt for a 72pt image).
Both places must skip the pin. Pinned by `ReplacedGridItem_KeepsItsNaturalSize_UnderJustifySelfNormal`
(both `<img>` and `display:block`) and `..._WithExplicitJustifySelfStretch_StillStretches`.

**Review corrections, all three found by the maintainer.** (1) The first version only handled `normal`:
under `start`/`center`/`end` (as `-self` or `-items`) the item was still pinned to the track, stretched,
and with `align-items:center` + `justify-self:center` overlapped its neighbour. The measure-time test is
now "anything but an explicit `stretch`". **A mutation test is what makes this stick, and the
single-axis cases were not enough:** reverting to the `normal`-only test survived every case that set
one axis, and was only killed by the combined ones (`align-items:center` with `justify-self:center`;
`justify-items:end` with `align-items:end`), so those are in the theory. (2) `<img>`/`<svg>` were
assumed to have a natural size. `SvgIntrinsicSize.Resolve` sizes a `viewBox`-only svg from its viewBox so
it has something to lay out at, but CSS gives it an aspect ratio and no natural size, and browsers
stretch it; `SvgIntrinsicSize.HasNaturalSize` now answers per axis (own attribute, or the other one plus a
`viewBox`). (3) **A trap in (2):** at the time the grid asks, an image is not loaded and an inline svg is
not built, so `Image`/`Document` are null and every natural-size check was false (the whole theory failed,
including the `normal` cases that had passed). `HasNaturalSizeAsync` awaits `MeasureWordsSize` first.

**Still open: a grid container as a flex item** is sized from the flex container, not its tracks
([gap](../accepted-gaps/a-grid-container-as-a-flex-item-is-not-sized-to-its-tracks.md), issue #1663).

**Evidence.** Full `PeachPDF.Tests` net8.0 suite green with the change.
`FlexGridItemBlockificationIntegrationTests`' computed-display assertions fail on the merge base.
