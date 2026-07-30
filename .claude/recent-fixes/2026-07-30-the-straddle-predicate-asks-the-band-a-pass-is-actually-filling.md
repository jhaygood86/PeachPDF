# The straddle predicate asks the band a pass is actually filling

Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320). Closes [#400](https://github.com/jhaygood86/PeachPDF/issues/400),
[#435](https://github.com/jhaygood86/PeachPDF/issues/435). Stage 2+3 of `#435`, following stage 1
([2026-07-30-a-pass-tells-its-cursor-when-it-places-content-past-the-band-it-opened-with.md](2026-07-30-a-pass-tells-its-cursor-when-it-places-content-past-the-band-it-opened-with.md)).

## The load-bearing idea

`HtmlContainerInt.BandBeingFilled` — stage 1's instrumentation-only shim — is flipped to actually
return `filling.Band` (the pass's own cursor, `FragmentainerContext.SlotIndex`) instead of the page
grid's answer, whenever a real fragmenting pass is filling a fragmentainer with no band of its own.
`CssRect.WouldStraddleFragmentainer` (the break decision) reads through it; `CssRect.BreakPage`'s
pure relocation is split into its own `WouldStraddleItsOwnBand`, which deliberately stays grid-based
— relocating a word into "the band after the one it's in" needs the grid's answer regardless of
which fragmentainer a pass is nominally filling, or an atomic-inline's vertical inset would relocate
a word two bands instead of one.

Two follow-on corrections were needed once real content started actually straddling for the first
time:

- **`CssLayoutEngine`'s `InlineBreakToken` resume slot** used to be `SlotStartingAt(word.Top) + 1` —
  correct only when the break truly falls at a band boundary, and one slot too many in the "spill"
  case stage 1 closes (content already past the band it claims to be filling). Replaced with
  `CssRect.ResumeSlotForBreakBefore()`, which asks whether the word's own bottom (plus its cloned
  block-decoration-break insets) falls past *the band its own top lands in* — byte-identical to the
  old expression in the ordinary case, and correctly `k` rather than `k+1` in the spill case.
- **`CssBox.PlaceBlockChild`'s root placement.** The document root has no predecessor to test §5.2's
  boundary against, so it never asked `BandBeingFilled` and never stepped the cursor at all — a gap
  the per-mechanism list in stage 1's own note didn't anticipate, because it only became observable
  once the predicate itself started reading the cursor. A `StepOverTo` was added for exactly this
  case.
- **`CssBox.LayoutBlockChildren`'s block-overflow `StepOverTo`** (added in stage 1, gated on
  `MonolithicContent.IsMonolithic`) had to be generalized to fire for *any* finished, in-flow block
  child that overflows past the fragmentainer being filled, monolithic or not. The narrow gate was
  correct for stage 1 (the predicate wasn't reading the cursor yet, so a stale cursor there cost
  nothing), but became a source of new failures once the predicate flipped: an ordinary tall filler
  pushing later content past a boundary needs the same cursor truthfulness a monolithic box does.

## What was found by running it, not by reading it

**Orphans/widows (default 2) were not reliably engaging for ordinary multi-page paragraph overflow
before this fix.** Without a real break token — which the stale predicate never produced for
"normal" overflow, only for a genuine forced break or an already-monolithic case — the
widows-correction mechanism had nothing to act on. This fix makes it correctly functional for
ordinary paragraphs for the first time, which is a genuine spec-compliance improvement, not merely a
side effect to neutralize; one existing test
(`InlineSpanningAPageBreak_KeepsCountingEdgesAcrossPages`) needed `widows:1;orphans:1` added to its
fixture to keep testing the edge-continuity behavior it was written for, now that the default
widows correction is actually reachable there.

**A sixth cursor-truthfulness site, missed by stage 1's own enumeration, caused a real content-loss
defect.** The `flexbox` showcase's corpus diff (page 2 of a demo table) lost a row's label and
description text entirely on repaint, while its middle cell painted fine. Root-caused through: (1)
`pymupdf` text extraction confirming the words were genuinely absent from every page, not just
misplaced; (2) fragment-tree inspection proving layout's own word-claiming was correct — every word
claimed by the right slot; (3) a full-pipeline repro through the public `PdfGenerator.GeneratePdf`
API isolating the defect to paint, not layout; (4) paint-side diagnostics tracing the word's
`fragment.OverflowClip` (from the cell's own `overflow: hidden`) to a rectangle built from the
cell's *live* `Bounds`, which sat visibly below the cell's own child word's position — geometrically
impossible for settled layout; (5) instrumenting `HtmlContainerInt.BandBeingFilled` directly and
comparing `FragmentainerContext` instance identity at each layout attempt, which showed the row's
own content asking its fragmentation questions against a *different, stale* context instance than
the one the table's whole-table relocation had actually advanced.

The cause: `LayoutBodyRows`' two whole-table relocation pre-checks ("move the entire table to the
next page when the first body row would cross a page boundary and the full body fits one page")
move the table via `TableRowCursor.RestartAt`, which — unlike its sibling `MoveToSlot` — never
called `StepOverTo` on the document-level fragmentainer cursor. Before this fix, that omission cost
nothing (`BandBeingFilled` still answered from the grid regardless). Once the predicate started
reading the cursor, a row laid out at the relocated table's own top asked its fragmentation
questions of the band the table had just left, read a spurious straddle against a band already
behind it, and deferred every word of its first row to a same-slot "resume" that then landed with no
vertical alignment applied and an ~3pt reported height instead of the row's real ~21pt. Fixed by
giving `RestartAt` the same optional `HtmlContainerInt? container` parameter `MoveToSlot` already
has, stepping the cursor the same way.

## What was deliberately not done

- **`CssBox.PlaceBlockChild`'s §5.2 test was only taken to its "3a" sub-step** (`Math.Max(prevSlot,
  filling.SlotIndex)`), not reduced outright to `filling.Band` (the plan's "3b"): the block-overflow
  generalization above is what "3b" was gated on, and it landed as part of this same change, so 3b
  applies here too — `band = filling.Band` outright, dropping the coordinate read.
- **The two remaining tiny corpus diffs** (`background_origin_clip`, `gradients`) were not chased
  further than confirming their shape: both are a uniform sub-point cascade on one page, starting
  partway down it and continuing to the page's end, matching mechanism 1 (a tolerance-boundary line)
  becoming a real break exactly where the design intends. Both renderers agree on the (sub-pixel)
  result.

## Evidence

Full `net8.0` suite green (7,027 passing, 9 skipped, 0 failed). Zero-warning solution rebuild. 100%
diff coverage against `origin/main`. 73 of 76 showcases byte-identical; the 3 that differ were each
rasterized with both PDFium and MuPDF at 150dpi and read: `flexbox` itself is now byte-identical
(the content-loss defect above is what the corpus diff was catching); `paged_media_monolithic_content`
page 3 shows a genuine improvement — a monolithically-relocated card that previously reported extra,
unused blank space inside its own border now reports its content's real height; the other two are
the sub-point cascades described above.
