# Atomic inline-level boxes place their block-level content instead of losing it

Closes [#473](https://github.com/jhaygood86/PeachPDF/issues/473). Found while investigating whether
[#18](https://github.com/jhaygood86/PeachPDF/issues/18) could be closed: the reporter's document uses
`<table style="display:inline-block">` (a common trick to keep a table's width to its content rather
than stretching full-width) for a small "PDF / Number / Date" info block, and it rendered with two of
three rows missing entirely and the third's cells scattered across lines.

## The fix, in two halves

`DomParser.IsAtomicInlineLevel` (~line 2646) previously named only `inline-flex` as an atomic
inline-level box (CSS Display 3 §2.3) with a layout path of its own; `inline-block`, `inline-table` and
`inline-grid` were left out on purpose (see the deleted accepted-gap file), because naming them there
alone stops `CorrectBlockInsideInline` from incorrectly hoisting their block-level content out — but
`CssLayoutEngine.FlowBox` had no atomic-placement branch for any of the three, so their content fell
into the generic recursive path, which places nothing at all for a block-level child. Closing #473
needed both halves together, done in the same change:

1. **`IsAtomicInlineLevel`** now names all four atomic inline-level display values.
2. **`CssLayoutEngine.FlowBox`** gained a new dispatch arm (parallel to the existing `inline-flex` one)
   and a new helper, `FlowAtomicBlockContentChild`, that positions the box and lays its content out via
   `CssBox.LayoutContentAtItsAssignedPosition` — reusing `CssBox.LayoutContents`'s own already-correct
   per-display dispatch (table/grid engines, or ordinary block children) rather than duplicating it.
   `inline-table`/`inline-grid` are routed unconditionally (their structural children are never inline
   content regardless of what's inside them); `inline-block` is routed only when a new
   `HasBlockLevelDescendant` check finds a real block-level box somewhere in its subtree — the ordinary,
   already-working case (inline-block holding only text/inline content) is untouched.

## Two traps found by actually running it, not by reading the diff

- **`LayoutContentAtItsAssignedPosition` must NOT be wrapped in `DetachFragmentainer`/
  `SuppressWordPageBreaks`.** The obvious-looking "treat this as monolithic content" wrapper (mirroring
  `CssBox.LayoutContents`'s own genuinely-monolithic branch) silently broke everything: layout completed
  with a fully correct box tree — every cell's `Location`/`Words` exactly right — but the PDF's content
  stream carried zero text objects. `FragmentEmitter`'s own recording
  (`CurrentFragmentainer is not { IsFragmenting: true } → return`) skips emitting anything at all while
  the fragmentainer is detached; that guard exists for real measurement/monolithic passes whose content
  is never meant to reach the page, not for this box's real, on-page content. Found by dumping the box
  tree directly (a `HtmlContainerInt` + `PdfSharpAdapter` harness) against the *actual* generated PDF's
  extracted text (PyMuPDF `page.get_text()`) — the two diverging completely was the tell. Removed; this
  helper relies on the ambient fragmentainer being live the same way the sibling `FlowInlineFlexChild`
  (which calls its engine directly, no wrapping at all) already does.
- **The routing guard cannot be a shallow "are my direct children all inline" check.** A
  `<table style="display:inline-block">`'s `<tr>` rows get wrapped in a synthesized, tag-less
  `Display=InlineTable` box by `DomParser.CorrectAnonymousTablesGenerateMissingParents` (an inline-block
  table isn't `CssBox.IsBlock`, so its rows don't get a block-typed wrapper) — so the *outer* table has
  only ONE direct child, and that child is itself `CssBox.IsInline` (`InlineTable` is in that set). A
  shallow `DomUtils.ContainsInlinesOnly` check is fooled by that wrapper into reporting "purely inline
  content" for a box that in fact holds a whole table's worth of block-level rows one level further down
  — so the outer box never reached the new atomic branch at all, and separately never got its own
  `ActualRight` resolved (nothing else on this path sets it for a box reached this way). `FlowBox`'s new
  `HasBlockLevelDescendant` helper looks *through* nested atomic wrappers instead of stopping at them
  (the opposite of `ContainsInlinesOnlyDeep`'s own atomicity short-circuit, which is correct for the
  DOM-fixup question but wrong for this one) to find the real `<tr>` underneath, and
  `FlowAtomicBlockContentChild` now resolves `ActualRight` for `inline-block` unconditionally (explicit
  length/percentage via `GetBoxWidth`, `auto` via the existing shrink-to-fit refinement), not only when
  the width is `auto`.

## Scope decisions

- The new atomic branch treats its box as monolithic within the surrounding inline flow (mirroring the
  existing `inline-flex` branch's own `childOpensHere` guard) rather than attempting genuine mid-line
  fragmentation of an inline-table/inline-grid/inline-block's own content across pages — a materially
  harder, separately-scoped problem.
- Two further defects surfaced while verifying against the real issue #18 document, both closed in the
  same change (not folded into the same commits above, but landed together):
  - `CssBox.ContainingBlock` walked past a non-`IsBlock` atomic inline-level ancestor (`inline-table`/
    `inline-block`/`inline-grid` aren't `IsBlock`) straight to the next real block ancestor, so a
    percentage width on a `<td>` inside a `<table style="display:inline-block; width:65%">` resolved
    against that wider ancestor instead of the table's own narrower width, overflowing the table's own
    border. Fixed by adding those three (plus `Grid`, for consistency with the `Flex`/`InlineFlex` stop
    already there) to `ContainingBlock`'s stop-list — each already has its own resolved content box and
    establishes the containing block for its own normal-flow descendants per CSS 2.1 §10.1, the same way
    `Flex`/`InlineFlex` already did.
  - A `float` sibling of the table still visibly escaped the containing div even once the table itself
    laid out correctly. Root cause: `DomParser`'s anonymous-block correction leaves such a float as the
    *sole child of its own tag-less wrapper* — one level removed from being a direct sibling of the
    table's own wrapper — which defeated both `CssBox.FloatLineTop`'s rule-6 "immediately preceding
    sibling" lookup (the wrapper itself isn't `IsFloated`, only its lone child is) and
    `MarginBottomCollapse`'s "last in-flow child" pick (the wrapper isn't `IsExcludedFromFlow` either, so
    it out-competed the table's own wrapper for "last real content"). Fixed narrowly at both call sites
    with a new `CssBox.UnwrapSoleFloatChild` helper, rather than widening any of the DOM-correction
    classification predicates (`DomUtils.ContainsInlinesOnly`, `DomParser.ContainsVariantBoxes`/
    `JoinsTheInlineRun`) — three separate attempts at the predicate route were tried and reverted first,
    each fixing the target case while silently breaking a different existing behavior (a stack overflow
    from an unbounded re-wrap oscillation, an Acid2 margin-collapsing regression, and loss of
    `LayoutBlockChildren`'s dedicated float-vs-float collision avoidance/`clear` handling for an
    unrelated shape) — the classification predicates turned out to be load-bearing for other float
    shapes in ways a quick per-box reclassification couldn't see. Unwrapping only at the two call sites
    that actually needed to see through the pointless wrapper avoided all three.

## Evidence

New `AtomicInlineLevelBlockContentIntegrationTests.cs` (issue #18/#473 exact-shape 3×3 table repro,
the accepted-gap doc's own inline-block/inline-grid measurement case, a bare inline-grid-in-a-paragraph
case, an inline-block-with-only-inline-content regression guard, an anonymous-inline-table-survives
DOM-shape test, a `min-width` shrink-to-fit floor case, a `string-set` case, and the `ContainingBlock`
percentage-width case) plus `FloatLayoutRegressionTests.FloatSiblingOfAnInlineBlockTable_
StaysContainedInsideTheDiv` and a strengthened assertion in the pre-existing
`AtomicInlineLevelBoxCorrectionTests.DisplayNoneChildOfInlineTableBox_StaysNestedUnderItsRealParent`
(previously only checked its `display:none` sibling, which passed by an unrelated accident regardless of
what happened to the real cell content). Full suite green on net8.0 (10,355 passed, 0 failed, up from the
baseline's 10,345), 94.8% diff coverage on the changed lines, whole solution `dotnet build -t:Rebuild` at
0 warnings. Visually re-rendered the real issue #18 attachment end to end against a Playwright reference
render: all three info rows render in the correct grid, the float stays inside its container instead of
overlapping the data table below it, and percentage-width cells stay inside the table's own border.
