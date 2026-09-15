# A declared width sizes the inline-block, not just the line

Issue #1091. #951 had made `width` on an inlines-only `display: inline-block` move the line cursor,
deliberately without giving the box geometry; the box therefore still painted its background, border
and overflow clip at its content's width — nothing at all for an empty one. The load-bearing idea is
that there is exactly one used width, assigned once (`ResolveAtomicInlineDeclaredWidth` writes
`CssBox.Size`), and both the cursor advance and the painted rectangle read it back from there.

## What running it turned up

- **The vertical axis has to be left strictly alone, and the obvious shape does not.** The first
  version stated the box's whole rectangle at the advance point, in `FinalizeFlowBoxExit`, and let
  `BubbleRectangles`' later union widen it. That union is min/max on all four edges, so a rectangle
  built from `trueStartY`/`ActualHeight` pulled the top up from the ink to the line's flow top — the
  flow does not know where the box's content finally settles vertically, and `ApplyVerticalAlignment`
  had not run yet. Splitting it fixed that: the flow states a rectangle only for a box that visited
  no word at all (there is nothing to union with), and `WidenAtomicInlineRectangles` — a new step
  between `BubbleRectangles` and `ApplyVerticalAlignment` — widens every other one *in place*,
  keeping the `Y`/`Height` the words produced. Anchoring on the bubbled rectangle's own `X` also gets
  `text-align` for free: that shift has already happened by then.
- **`PlacedSince(startOrdinal)` is not "did this box place a word".** It reads
  `fromOrdinal >= ResumeOrdinal`, which is unconditionally true on any pass with `ResumeOrdinal == 0`
  — it answers "is this pass responsible for this content", for fragmentation. The empty-box branch
  needs `coordinates.WordOrdinal == startOrdinal` instead. With the wrong predicate the empty case
  silently produced no rectangle at all while every other assertion still passed.
- **Removing the old parent-loop advance broke the *other* path, and only the showcases said so.**
  That `Math.Max(CurrentX, childContentStartX + declared)` sat outside the `if`/`else if` dispatch, so
  it also ran after `FlowAtomicBlockContentChild` — and it was compensating a bug there:
  `FlowAtomicBlockContentChild` advances to `b.ClientRight`, which comes out one padding short of the
  declared content box. Dropping it made `cascade_layers`' 150px cards advance 121.5pt instead of
  145.5pt, which changed the document's width enough to stop triggering shrink-to-fit — visible as
  the *whole page* rendering at a different scale. The full test suite was green throughout. The
  advance is restored (now fed by `ResolveAtomicInlineDeclaredWidth`'s return value, since
  `FlowAtomicBlockContentChild` overwrites `Size`); `FlowAtomicBlockContentChild`'s own short advance
  is left alone, still compensated, and is not this issue's.
- **`Size.Width` is the raw declared length under either `box-sizing`.** `Size.Width` means whichever
  box that property selects, and `ActualBoxSizeIncludedWidth` adds the padding and border back only
  under `content-box`. So one assignment covers both, and `AvailableWidth` reads back the content
  width — which is what the line reserves, since the caller applies the leading spacing before and
  the trailing spacing after. No second box-sizing calculation anywhere.
- **#1094 landed on `main` first and rewrote the same branch, which changed what is left to do.** Its
  fix compares a content advance against `Size.Width` (rather than the border-box `ActualWidth`) and
  anchors the rectangle at the border-box left — the two box-model errors this change had also found,
  fixed generically for every inline. Two things still separate an atomic inline from it, and they are
  why `ReserveAtomicInlineUsedWidth` exists rather than the branch simply being reused: `Size.Width` is
  a *border*-box width under `box-sizing: border-box`, so it is not what to reserve between a
  content-box left edge and a trailing spacing added afterwards (`AvailableWidth` is, under either);
  and that branch states the rectangle whenever the content came out narrower, which reintroduces the
  block-axis merge above for a box whose words *did* land on the line. After #1094 an empty padded
  `auto`-width inline-block advances the line correctly and paints nothing at all — the rectangle is
  what this change adds back.

## What was deliberately not done

The percentage `width` exclusion stayed at the time of this fix; it was subsequently closed by #1097.
Routing every inline-block through `FlowAtomicBlockContentChild`, which would make it genuinely atomic,
remains a far larger change and is what #1032/#1053/#771 circle around.

## Evidence

`InlineBlockDeclaredWidthGeometryTests` (14 fixtures: the issue's three shapes plus padding/border,
the fragment tree carrying it, the border-box left edge, filled-vs-empty parity, `box-sizing:
border-box` and its floor, overflow not cut back, the percentage exclusion, `text-align` carrying the
stated rectangle, the empty auto-width box, and a nested empty child). Twelve of the fourteen fail on
`main` (at 96dd810f, i.e. with #1094 already in); the two that pass are the exclusion guards. Full
suite green on net8.0 (11,691), 100% diff coverage, solution rebuild with 0 warnings.

All **129** existing showcases come out byte-identical in their decompressed content streams — which
is the real check here, since the suite stayed green through the regression above. A new
`atomic_inline_width` showcase covers the shapes that changed, verified through both PDFium and
MuPDF.
