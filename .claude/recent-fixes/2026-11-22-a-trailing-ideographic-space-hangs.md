# A trailing ideographic space hangs

css-text-3 section 4.1.3: an ideographic space (U+3000) at the end of a line hangs, as a preserved space does. It does not count toward the line's fit, the line's content width or `max-content`, and the next word still wraps. Not under `white-space: break-spaces`, where it wraps like any space.

**How:** U+3000 is not collapsible (see [2026-11-22-only-space-tab-and-segment-breaks-collapse.md](2026-11-22-only-space-tab-and-segment-breaks-collapse.md)), so it sits in a word (UAX #14 gives it break-after, so `う　` is one word or `う` + `　`). `CssLayoutEngine.MeasureTrailingIdeographicSpace` is the width of the U+3000 run ending a word. It feeds three places:
- the horizontal fit in `FlowBox` (an overflowing word whose trailing space fits hangs it via `HungEndWidth`; a word of nothing but U+3000 after a word that hung hangs too, so a run of them never opens a line);
- `ApplyEndHang` at line close, which sets `HungEndWidth` over the whole trailing run of U+3000-only words so alignment and `LineContentEnd` skip it;
- the max-content walk in `CssBox` (`trailingSpace`), which accumulates a run of U+3000-only words.

**Trap:** `GluedRunWidthAfter` (see [2026-11-22-a-word-cut-by-font-fallback-wraps-with-its-glued-fragments.md](2026-11-22-a-word-cut-by-font-fallback-wraps-with-its-glued-fragments.md)) used to stop at any space word; a U+3000 word is one, so under `break-spaces` the space never travelled with the character before it. It now counts U+3000 words minus what hangs.

**Evidence:** css-text 841 -> 851 on a Mac, nothing lost, other modules unchanged (floor 815 -> 825). Still failing: `trailing-ideographic-space-001/002/005/010` differ by a one-pixel sliver because the fallback CJK font's U+3000 advance is not one em; the `break-spaces` ones are not looked at. Engine (20,438) and PeachPDF suites pass.
