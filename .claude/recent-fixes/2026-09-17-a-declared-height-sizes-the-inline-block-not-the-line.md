# A declared height sizes the inline-block, not the line

Issue #1101, the block-axis sibling of #1099 (width). An `inline-block` whose own content is
inline-only (or empty) is flowed into the surrounding block's line boxes rather than laid out as a
box of its own, and nothing on that path ever resolved a declared `height`/`min-height` into
anything — `CssLayoutEngine.ApplyHeight` (the only place that happens) is reachable only through
ordinary block-child layout, which this box never goes through. So the box's own painted rectangle
came out at whatever height its content happened to measure, same symptom #1099 fixed for width.

The load-bearing idea is a new pair of methods mirroring the width fix's shape —
`ResolveAtomicInlineDeclaredHeight` (pure, computes the declared/used border-box height from
`height`/`min-height`, or `null`) and `HeightenAtomicInlineRectangles` (grows a box's per-line
rectangle to that height, never shrinking) — but the two do **not** mirror the width fix's own
architecture as closely as they look: `ResolveAtomicInlineDeclaredWidth` writes `CssBox.Size` early,
before the box's content is placed, because the line's cursor advance needs the resolved value in
the same pass. Height has no such same-pass consumer to serve (the only candidate, `FinalizeFlowBoxExit`'s
`MaxBottom` line-height correction, is deliberately left alone — see below), so this fix never writes
`CssBox.Size` at all; both new methods are a pure, self-contained correction over
`CssLineBox.Rectangles`, run once, late.

## What running it turned up

- **Where the correction runs relative to `ApplyVerticalAlignment` matters far more than it does for
  width, and in a way that isn't simply "before or after."** The first version ran
  `HeightenAtomicInlineRectangles` between `BubbleRectangles` and `ApplyVerticalAlignment` — the exact
  slot `WidenAtomicInlineRectangles` uses — reasoning that `AtomicInlineBaselineOf`'s `overflow`-not-
  `visible`/empty-box branches read the rectangle's `Bottom` for the baseline itself, so they need the
  grown value. That much was right. What it missed: `ApplyVerticalAlignment`'s baseline-extent loop
  folds *every* baseline-aligned atomic inline's whole margin box into the line's shared extent
  (CSS 2.1 §10.8.1), unconditionally, regardless of which of `AtomicInlineBaselineOf`'s branches
  produced the baseline point — so a grown rectangle also grows the *line*, genuinely reserving the
  declared height on it. That is correct per spec, but it is exactly the sibling gap (issue #1166)
  this change means to leave alone, and it surfaced as a real bug rather than a merely-out-of-scope
  nicety: the containing block's own height is computed earlier, from `FlowBox`'s `MaxBottom` — before
  `FinalizeLineBoxes` (and this correction) ever runs — so growing the line here left the block itself
  too short, and its next sibling overlapped the now-taller line.
- **Running strictly after `ApplyVerticalAlignment` avoids that, but reopens a much older, unrelated
  bug that turned out to be latent in the engine already: an inline-block whose `width` comes from an
  inline `style` attribute (rather than a stylesheet rule) and whose content needs to wrap can be laid
  out twice** — once via the ordinary inline-flow path, wrapping across several of the *parent's* own
  lines, before the engine notices it should have been routed to `FlowAtomicBlockContentChild` and
  redoes it correctly — and the first attempt's now-abandoned `CssLineBox` rectangle entries are never
  removed from `CssBox.Rectangles`, so both the stale (small, wrongly-positioned) and the correct
  rectangles get painted. This is pre-existing and reproduces with `main`, with no `height`/
  `min-height` involved at all — confirmed by reproducing it on the already-shipped, already-tested
  `wrapbox` showcase fixture merely by moving its `width: 120pt` from the stylesheet rule into an
  inline `style` attribute. It was invisible before only because nothing had combined "wraps" with
  "inline-`style` width" in a document anyone looked at closely. Filed as **#1168** rather than
  fixed here (out of scope for #1101), and the new showcase section was written to avoid it (a
  `boxh-narrow` class instead of `style="width:…"`) rather than mask it.
- **Percentage `height`/`min-height` is excluded, unlike width after #1097.** Resolving one correctly
  needs to know whether the box's containing block itself has a definite height
  (`CssBox.IsHeightCalculated`), which is only ever set in that ancestor's own layout epilogue —
  strictly *after* this box's own content (and this correction) already ran, since a block lays out
  its content before its own epilogue. A percentage width doesn't have this problem because width is
  usually stretch-fit and known top-down before children are placed; height is normally resolved
  bottom-up from content, so the ordering genuinely isn't there yet. Matches `ResolveAtomicInlineDeclaredWidth`'s
  own original (pre-#1097) scope, and is recorded the same way
  (`.claude/accepted-gaps/percentage-height-on-an-inline-content-inline-block-is-ignored.md`).
- **Growing "downward, keeping the top" is only right for some `vertical-align` values, and a
  post-review attempt to generalize it made the primary case worse.** A post-change review caught
  that growing down from the box's post-alignment top only matches `top`-anchored cases (`vertical-
  align: top`, or the default `baseline` on a box whose own word ascent is the baseline). An explicit
  `bottom`/`middle`/`sub`/`super`/`text-top`/`text-bottom`/length offset, or `baseline` on an empty or
  `overflow`-non-`visible` box (whose bottom margin edge stands in for its baseline instead), anchors
  a *different* edge. An anchor-aware rewrite correctly fixed the keyword cases — but for the
  bottom-margin-edge `baseline` case, computing "the anchor" required reading
  `AtomicInlineBaselineOf`'s `rect.Bottom`, which is itself the box's still-*natural* (pre-growth)
  rectangle; growing "up" from that already-wrong position moved the box further off than never
  growing it at all (measured: `TheEmittedFragmentCarriesTheDeclaredHeight`'s empty bordered box came
  out at `Y = -20` instead of `20`). Fixing that case for real needs the grown height known *before*
  `ApplyVerticalAlignment` runs at all — the same "sizes the line" gap the first bullet above already
  reopens. Reverted to the simple, always-downward growth and recorded the narrower gap as **#1169**
  rather than trade one wrong direction for another.
- **`min-height` needed its own combining rule, not `GetBoxHeight`'s.** `GetBoxHeight`'s `auto` baseline
  is `box.ActualBoxSizingHeight` (`Size.Height` plus insets), which is meaningless here since
  `Size.Height` is never assigned on this path — calling it directly for a `min-height`-only box would
  floor against a near-zero baseline instead of the box's real (word-derived) natural height, and could
  *shrink* content that's already taller than the floor. `ResolveAtomicInlineDeclaredHeight` therefore
  computes its own value from `CssValueParser.ParseLength`/`IsValidLength` directly (the same primitives
  `GetBoxHeight` uses, not a second parser) and the caller only ever compares the result against the
  box's real, already-measured rectangle height — combining them by simple `Math.Max`, never shrinking.

## What was deliberately not done

- The line still doesn't reserve a declared height — tracked as **#1166**, with its own accepted-gap
  file. `docs/html-css-support.md` already described this gap before this change; the fix only adds
  the painted-box half of the picture alongside it.
- Percentage `height`/`min-height` stays a no-op, mirroring width's own original scope; see the new
  accepted-gap file for why and what closing it would need.
- The pre-existing inline-`style`-width-plus-wrap duplicate-rectangle bug (found above) is unfixed;
  it predates this change and reproduces with `main` on an already-shipped showcase fixture, unrelated
  to height/min-height.
- Growth only ever extends downward, which mispositions a box under a non-default `vertical-align`
  (or an empty/`overflow`-hidden `baseline` box) whose declared height exceeds its natural content —
  tracked as **#1169**, with its own accepted-gap file.

## Evidence

`InlineBlockDeclaredHeightGeometryTests` (14 fixtures across 10 test methods: the issue's own repro
table, `min-height` parity with `height`, `min-height` as a floor that doesn't shrink taller content,
the border-box sizing case and its own floor, the percentage exclusion, the fragment tree carrying
the grown height, growth landing below the content rather than above it, filled-vs-empty parity, and
a nested empty child). One new paint-level regression test added to
`InlineBlockOverflowClipPaintTests` asserting (via content-stream adjacency, per this repo's testing
convention for graphics-state features) that an `overflow: hidden` box's clip grows to the declared
height, not the one-line content height. Full net8.0 suite green (12,232 passed, 9 platform skips),
100% diff coverage on the 70 changed lines, solution rebuild with 0 warnings. All showcases
regenerated and their decompressed content streams compared against `main`; only `atomic_inline_width`
(extended with two new sections) differs, rasterized through both PDFium and MuPDF.
