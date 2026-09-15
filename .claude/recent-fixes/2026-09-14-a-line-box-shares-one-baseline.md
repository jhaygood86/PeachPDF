# A line box shares one baseline, and the leading is split around it

Found by rendering the `marker_styling` showcase against headless Chrome on the same HTML. Two cells
were wrong — `::marker { font-size: 13pt }` left the marker's digits sitting below the text they
numbered, and `list-style-position: inside` with a 20pt marker put the item's first line 12.8pt
higher than Chrome's — and both turned out to be the same defect, with nothing to do with `::marker`.

## The load-bearing idea

`CssLayoutEngine.ApplyVerticalAlignment` computed `baseline = Math.Max(rect.Top)`. The variable was
called `baseline`; the quantity was the lowest **top edge** on the line. Every box was then placed
flush with it. Top-alignment and baseline-alignment coincide exactly while every font on a line is
the same size, which is why this survived so long — and `line-height: normal` resolves from the same
ascent + descent + gap that `RFont.Height` is built from, so the leading is ~0 there too and nothing
moved for an ordinary paragraph either.

Three things were missing, and they are one rule
([CSS 2.1 §10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height)):

1. **Half-leading.** An inline box's content area is centred in its `line-height`, not hung from the
   top. `line-height: 3` centres the text in its band; we put it at the top.
2. **A real baseline.** Every box on the line hangs its own content area from one shared baseline.
3. **A line box is a pair of extents, not a height.** The box reaching highest above the baseline
   need not be the one reaching lowest below it, so `max(above) + max(below)` can exceed *every*
   `line-height` on the line. Chrome makes `30pt/30pt` text containing a `10pt/40pt` span 56.328px
   tall — more than either the 40px or the 53.328px line-height on it. A max-of-line-heights model
   cannot produce that number.

`LineBoxExtent` (new, `Html/Core/Entities`) is the pair; `LineBoxContributionOf` returns one inline
box's share and `CssLineBox.BaselineExtent` accumulates the line's. It is **nullable on purpose**:
either side can legitimately be negative, and accumulating into a zeroed pair floored it at zero,
which made a short `line-height` line taller than it had declared (this is what
`FirstLinePseudoElementIntegrationTests.SmallerFirstLineHeight_ReducesTheFirstLineBox` caught).

An `outside` `::marker` is not in its item's inline flow, but it does sit on that line's baseline, so
`OutsideMarkerExtentOf` unions its contribution into the item's first line. css-lists-3 §3.5 leaves
both the alignment and the line-height interaction *expressly* undefined ("this is handwavey nonsense
from CSS2, and needs a real definition"), so this follows browsers, not a rule.

**Only the marker's ascent side is unioned in, and that was measured rather than assumed.** Chrome on
`<li>Item` at 8.5pt with `::marker { font-size: 20pt }`: the line's extent above the baseline goes
from 10px to 24px — the marker's whole ascent — while below it stays at the item's own 2px rather than
growing to the marker's ~5.7px descent. The marker hangs outside the principal box, so its descender
has nothing under it to push down. Unioning both sides (the first version) made the showcase's
three-item band 74.5px against Chrome's 68; ascent-only brings it to 66.5.

## Placing the ink at flow time, not after it — and why that is not optional

The first working version did all of this in `ApplyVerticalAlignment`, after the line closed. Tests
passed except one: `MulticolLayoutIntegrationTests.AtAnAvoidColumnBreak_…` reported 11 claimed words
but 9 distinct ones. **Two words were being painted in two columns each.**

The reason is that fragmentation decisions — `WouldStraddleFragmentainer`, `OverflowsEveryFragmentainer`,
and the emitter's own `ClaimsWord` — are all asked of **the word's own rectangle**. Judging a word to
fit while it sat at the line's top and only *then* moving it down by the half-leading put it across
the boundary after the decision had been made. So `FlowBox` now applies `HalfLeadingOffsetOf` the
moment the word is placed, before those questions are asked; `ApplyVerticalAlignment` re-derives every
offset from the closed line and remains the authority, and for a line whose fonts are all one size the
two agree exactly and it has nothing left to do.

**That change broke an unstated invariant, and finding it was the expensive part.** A good deal of
this engine read a word's own top as though it were its line's — `DropATrailingForcedBreaksOwnLine`
assigning `MaxBottom = last.Words[0].Top`, and `CssLineBox.LineTop` (min rectangle top), which is what
`CssBox`'s orphans/widows slot checks and `HtmlContainerInt`'s fragment invalidation ask. Once a word
sits half a leading below its line, every one of those is off by that much. 12 tests failed at once,
across four unrelated areas. `CssLineBox.FlowTop` now records the line box's own top as the flow
places it, `LineTop` prefers it, and all 12 went green together. **If you add a reading of "where does
this line begin", read `FlowTop`/`LineTop` — never a word.**

## What the review pass found, and one thing it uncovered underneath

Three findings, all real, all now pinned by tests that fail against the un-fixed code:

- **`CssLineBox.BaselineY` did not carry the negative-leading floor.** The `escape` shift moves every
  box on the line down; `BaselineY` was computed before it and kept naming a baseline the boxes no
  longer sat on. `CssBoxMarker` reads it, so a `20pt/10pt` list drew its marker half a negative leading
  above the text it numbered. It is now corrected by `escape` at the same moment the deltas are.
- **`vertical-align: top`/`middle`/`bottom` aligned to the topmost *ink*, not to the line box.**
  `lineTop`/`lineBottom` were derived from `lineBox.Rectangles`, which now sit half a leading inside
  the line. They are seeded from `FlowTop` and `FlowTop + BaselineExtent.Height` instead, with the
  rectangles still folded in because replaced content grows the line without contributing to its
  baseline extent and can reach past either edge. **This was a genuine regression** — before the
  change the ink and the line box coincided, so the old reading was right by accident.
- Two duplicated `<summary>` blocks in the test edits, from a doc block inserted above the wrong
  member.

Fixing the second uncovered a **pre-existing double application**: a `<td>`'s own `vertical-align` was
being read *both* by the table algorithm (CSS 2.1 §17.5.3, `ApplyCellVerticalAlignment` — aligning the
cell's whole content in the cell) *and* by this inline pass, because `styledBoxForVerticalAlign`'s walk
stops at the first box with an `HtmlTag`, which for text directly inside a `<td>` is the cell itself.
It was invisible while inline `top`/`bottom` were no-ops on a single-box line, and surfaced as
`VerticalAlignIntegrationTests.Middle_OnATableCellWithExplicitHeight_CentersShortContent` missing its
own midpoint by 0.09pt — exactly half a half-leading. The inline pass now reads a table cell's value as
`baseline`; `vertical-align` is not inherited (§10.8.1), so a cell's value reaching that point can only
ever have been the cell's own.

## Follow-up: replaced and atomic inline content

Issue #1053 is now closed by the same shared-baseline path. A replaced atomic word (`<img>`, inline
`<svg>`, MathML, or a form control) contributes its complete margin-box extent above its bottom
margin-edge baseline, then moves that edge onto `CssLineBox.BaselineY`. An inline ancestor containing
only the replaced word takes the same precomputed delta, keeping its bubbled background/border
rectangle coupled to the child. `BaselineAlignmentLayoutIntegrationTests` covers every replaced word
type, a non-zero image margin, positive leading, and the wrapper case.

## Deliberately not done

- **Negative leading overflows downwards only** — see
  [`../accepted-gaps/negative-leading-does-not-lift-ink-out-of-its-line-box.md`](../accepted-gaps/negative-leading-does-not-lift-ink-out-of-its-line-box.md).
  The floor is line-wide, so the shared baseline is preserved exactly; only the whole line's ink
  moves. Without it, a line landing at a page's content top has its words claimed by the page above —
  or by neither, and they vanish.
- **`MaxBottom` is still not recomputed after `ApplyVerticalAlignment`**, so `vertical-align` still
  does not feed back into the block's height. Pre-existing and untouched.

This supersedes the "no half-leading, and no real baseline alignment" bullet in
[`2026-09-14-a-line-box-is-its-line-height-not-its-tallest-glyph-box.md`](2026-09-14-a-line-box-is-its-line-height-not-its-tallest-glyph-box.md),
which is the change that made the line's *height* right and left its *contents'* placement alone.

## The seven tests that changed, and why none of them was loosened

All seven asserted **glyph-ink** positions where they meant **line-box** positions — a distinction
that did not exist while ink was flush with the line's top. `LayoutHarness.LineTopOf` is the shared
helper they now use. Two are worth calling out:

- `FragmentEmitterTests.InlineSpanningAPageBreak_…` now fits **two** lines on page 0 rather than
  three. The third line box spans 60–90pt against a page ending at 80pt, so it does not fit and moves
  whole (css-break-3 §4.1). It used to be kept because its *glyphs* fitted — at `line-height: 30pt`
  over a 10pt font the ink ended well inside the page. That is a correctness improvement, not churn.
- `TableSpannedBandRepetitionTests` compared a `position: fixed` span's ink to its declared `top`.
  `top` positions the span's **box**; the glyphs sit half a leading lower inside their line box, and
  always did — the offset was simply zero. It now asserts cross-page identity (the test's own stated
  point) plus that the ink starts at `top` and within one line box of it.

## Evidence

- New `BaselineAlignmentLayoutIntegrationTests` (10 tests — 8, plus one each for the first two review
  findings, both confirmed to fail with that finding's fix backed out). Confirmed against neutered code (the
  half-leading offset, the alignment delta and the marker growth each stubbed out) that **5 fail** —
  the baseline sharing, the half-leading centring, the independent-maxima line height, and both
  marker cases — while **3 pass both before and after**, which is what makes them guards rather than
  restatements: the negative-leading floor, `line-height: normal` not moving, and replaced content
  staying at the line top.
- Every expectation was taken from headless Chrome on the same markup first: `d1` baseline 36px
  shared by both probes, `d2` 40px tall with its baseline 24px down, `d3` 7px, `e1` 56.328px, and the
  `<li>` 12px → 26px with the marker override. Values that depend on the resolved font (which is not
  Chrome's here) are asserted as relationships, each preceded by the metric precondition that makes it
  non-vacuous, so none can pass on a fallback font that happens to be the right shape.
- Full suite: `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` — 11,414 passed,
  0 failed, 9 skipped (pre-existing platform skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- `diff-cover` against `origin/main`: **100%** on the changed lines. Getting there found real dead
  code — `BaselineAlignment.GetFirstBaselineY` and `CssLineBox.AboveBaseline`/`BelowBaseline` were
  left unused by the final shape of the fix and were deleted rather than covered.
- Rasterized against Chrome's own print-to-PDF of the identical HTML (PDFium, 3×): the mixed-size
  line, both `::marker` cells of the showcase, and `line-height: 1/3/0.5` all match, the last
  differing only by the documented downward-only overflow.
