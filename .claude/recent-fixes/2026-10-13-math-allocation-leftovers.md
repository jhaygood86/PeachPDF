# Allocation left in the cascade, margin collapse and whitespace pass on the MathML benchmark

An allocation profile of math-l (174,000 boxes) after the earlier cascade work still showed per-box garbage in five places; the
CPU profile agreed (its "PollGC"/`BulkMoveWithWriteBarrier` frames are GC pauses charged to whichever allocation hit one, so
allocation volume is the thing to cut, not those frames).

- **UA / first-line / plain-author rule ordering** (`GetStyleRulesByOrigin`) still used the `OrderBy/ThenBy/Select` chain the
  author path lost earlier: ~120 MB per render of iterators, key arrays, delegates and a `ToList`. It now shares
  `SortKeys` with `SortForCascade` (`SortRules`), and `GetUserAgentStyleRules` returns the `List` so `DomParser` no longer copies it.
- `RulesUseRevertKeyword(List<LayeredStyleRule>)` wrapped the list in `Select` per box; indexed loop now.
- `FontVariationSettingsResolver.Encode` built a `StringBuilder` and a list for every box to conclude "nothing to encode"
  (`normal`, automatic optical sizing); that case returns first.
- `DerivedStyle.RequestedFontVariantPosition`/`ActualFontVariantPosition`/`SubSuperscriptSynthesis` read the `ShapingExtras`
  cache, creating it for every text box just to answer "no sub/superscript requested". They answer from the style first (the
  mapping is a three-way switch, so `RequestedFontVariantPosition` is no longer cached at all).
- `IsMarginCollapseThrough` allocated a filtered list per call; loop.
- `DomParser.CorrectTextBoxes` removed whitespace-only boxes with `List.RemoveAt`, shifting the tail each time - quadratic for a
  parent with thousands of children (a large `<math>`, a pretty-printed table). It marks and compacts once. The decisions look at the
  live neighbours above and below the box, so the loop tracks `nextLive` and the live count; a `display: none` child that is
  skipped must still update `nextLive` (it stays in the list), which is the easy thing to miss.

**Measured:** math-l 892 -> 698 MB allocated per render. Wall time on this machine is too noisy to quote (other jobs were running).

**Evidence.** A 150-document random differential of the whitespace pass against the old code (inline/block/text/br/img/pre pieces under
span, div and pre parents) was identical; Release suite, 19 benchmark documents, 199 fuzz documents and 220 showcase PDFs identical to main.
