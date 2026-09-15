# An inline-block that wraps is laid out as one box, and atomic inlines share the line's baseline

Issue #1053, plus three defects found while closing it. The load-bearing idea is that CSS 2.1
[§10.8.1](https://www.w3.org/TR/CSS21/visudet.html#line-height) has exactly one rule for where an
atomic inline's baseline is, and this engine had been answering it in two places that could disagree
— so it now lives in `CssLayoutEngine.AtomicInlineBaselineOf`, which `ApplyVerticalAlignment` reads
twice: once to size the closed line around the box, once to decide how far the box moves.

## What running it turned up, none of which reading it would have

Every one of these came out of the `baseline_alignment` showcase or a hand-built repro rendered
against headless Chrome on the identical HTML, then rasterized through both PDFium and MuPDF. The
suite was green for all four.

- **An `overflow: hidden` inline-block painted completely empty.** The box was given the margin-edge
  delta and the anonymous text box inside it a *font-derived* one, so the text ended up outside the
  box's own padding-edge clip and was clipped away entirely. An atomic inline is atomic: the delta
  walk in `BaselineShiftOf` now starts at the box and continues through its ancestors, so a
  descendant takes the enclosing box's shift rather than deriving its own.

- **An inline-block holding block-level content was aligned by its bottom margin edge.** §10.8.1 uses
  the baseline of its **last line box** whenever `overflow` is `visible`, which is what Chrome does —
  and what `main` did, by accident, having never moved such a box at all. This was a genuine
  regression in the commit this branch started from. `LastOwnLineBaselineOf` walks the subtree
  back-to-front for it, because the lines live on the block-level descendants, not on the box.

- **A box laid out atomically came back carrying one rectangle per internal line** — the shape an
  *inline* box needs so its border follows its content line by line. It painted its border once per
  internal line on top of the real one. `FlowAtomicBlockContentChild` drops them; its border box is
  the single rectangle it registers on the parent's line.

- **The box that establishes a line's formatting context was taking a place on its own line.**
  `FlowBox` iterates a block over itself, so an atomic inline-block appears among the rectangles of
  its *own* lines; reading its last line's baseline back there aligned every line inside it to the
  last one, spreading its content apart and out of its box. Both readers now stop at
  `lineBox.OwnerBox`.

## The multi-line case, and why routing it was the fix rather than aligning it

An inline-block whose content did not fit one line had its words handed to the **parent's** line
breaker, so they wrapped at the parent's measure: the box's content escaped it and its border box was
drawn as two disjoint line rectangles, the upper one landing across the text of the block above. No
amount of baseline arithmetic fixes that — the box has to own its lines.

`FlowAtomicBlockContentChild` already did exactly that for an inline-block holding block-level
content (#473), and `CssBox.LayoutContentAtItsAssignedPosition` dispatches inlines-only content to
its own inline flow without any help. So `LaysOutAsAnAtomicBox` widens the existing route rather than
building a second one: a box is atomic when it has a block-level descendant **or** when
`GetMaxContentWidth` exceeds the width it has to lay out in. Measured in border-box terms on both
sides, since `GetMaxContentWidth` reports the box's own border and padding with its content.

**Deliberately narrow.** A box whose content fits on one line keeps the flattened path exactly as it
was, which is what kept the blast radius to 18 of 131 showcases (all of them atomic-inline
alignment, verified individually) rather than rewriting how every inline-block in the repo lays out.

**The narrow-declared-width case now matches Chrome exactly**, which was a surprise: `<span
style="display:inline-block;width:20px">Wider than the box</span>` wraps one word per line inside a
15pt box, and an unbreakable word inside the same box overflows it without moving what follows —
both renders line up with Chrome's, including the overlap Chrome itself draws. Two tests asserted the
opposite (the box widening to its content), and were rewritten against Chrome rather than loosened.

## What was deliberately not done

- **An atomic box still never moves onto a line of its own when it does not fit** —
  [`../accepted-gaps/an-atomic-inline-does-not-wrap-onto-a-line-of-its-own.md`](../accepted-gaps/an-atomic-inline-does-not-wrap-onto-a-line-of-its-own.md),
  tracked as #1105. The showcase is what settled this: correcting the coupled `box-sizing` bug on
  that path in isolation makes `cascade_layers` **worse**, because at their correct width the cards
  no longer fit four to a row and PeachPDF has no wrap to move the fourth one down. The correction is
  therefore applied only to boxes routed here by their own inline content wrapping — where leaving it
  out would make one declaration paint two different widths depending on whether its text fitted.

## Evidence

- Full suite on net8.0: 11,725 passed, 0 failed, 9 skipped. `dotnet build PeachPDF.slnx -t:Rebuild` —
  0 warnings.
- All 131 showcases regenerated and compared against `main` by decompressed content stream: 18
  differ, each one an atomic inline taking the baseline (the inline image in `invoice`'s footer, the
  gradient-and-text pairs in `content_image`, the `list_style_image` markers, `mathml`, the SVG
  showcases), plus `baseline_alignment`, which gained the section that found two of the defects above.
- Chrome comparisons on the identical HTML for: a multi-line inline-block after a wrapped caption, an
  inline-block holding a `<div>`, a 15pt box holding breakable text, the same box holding one
  unbreakable word, and the four samples of `baseline_alignment` §5.
