# An absolutely positioned box in inline content stays out of the line (#1299)

**Symptom (#1299):** `<span style="outline: …">text<span style="position: absolute">x</span></span>`
drew no outline. Found while testing #1268's outline paint order.

**Cause:** not in outline painting. `DomParser.ContainsInlinesOnlyDeep` skipped floats but not
absolutely positioned children, and the cascade blockifies those. So `CorrectBlockInsideInline` split
the span around the child as if it were a block-in-inline, and hoisted the child out of the span. The
split clone is made by `InheritStyle(everything: true)`, which copied background and border but not
`outline-*`. The same split also put the text after the child on a new line, and took the child out
of a `position: relative` span, so the span was never its containing block.

**Fix:** absolutely positioned children are treated like floats (#1038) everywhere the parser decides
inline versus block content: `DomUtils.ContainsInlinesOnly`, `JoinsTheInlineRun`,
`ContainsInlinesOnlyDeep`, `ContainsVariantBoxes`, and the two "…InsideInlineBlockFormattingContexts"
walks, which need an arm so the child's own content still gets normalized. Layout then has to handle
the child inside an inline flow:

- `FlowBox` sets a non-inline absolutely positioned child aside (`CssLineBoxCoordinates.AbsolutelyPositioned`)
  and `CreateLineBoxes` lays it out after `FinalizeLineBoxes`, through `ParentBox.LayoutBlockChild`,
  the same entry the block path uses. It runs after the lines because the containing block may be an
  inline, whose fragments only exist then. On a break, only children before the resume ordinal are laid
  out; the ones on the discarded line are reached again by the resumed pass.
- `DomUtils.InlineContainingBlockOf` builds CSS 2.1 §10.1 (4.1)'s containing block from an inline's
  first and last `Rectangles` (border boxes, so borders come off; `rtl` swaps left/right). Every site
  that read the positioned ancestor's `Location`/`ActualWidth`/`ActualHeight` consults it first:
  `CommitBlockChildOffset`, the `right`/`bottom` fallbacks, `ResolvePositionedAutoBlockMargins`, and
  `GetBoxWidth`/`GetBoxHeight` (`CssLayoutEngine.InlinePercentageBase`). Block ancestors are untouched.
- `InheritStyle(everything)` now copies the four `outline-*` longhands, so a genuine block-in-inline
  split (and a repeated header clone) keeps its outline.
- `GetMinMaxSumWords` and `GetMinimumWidth_LongestWord` skip absolutely positioned children (§10.3.7).
- `CorrectBlockSplitBadBox` puts an absolutely positioned child that precedes a real block into the
  left piece of the split inline, so it stays inside the inline instead of being taken as the split point.

**Traps:**
- **Inline-level absolutely positioned boxes must still flow as words.** A `position: fixed` inline
  `<svg>`/`<img>` is not blockified, and has always been placed through `FlowBox`'s word path (the
  `excludedFromLineSpacing` and margin-shift code there). The first cut sent it to `LayoutBlockChild`,
  which sizes nothing for an inline box, and the SVG vanished (`SvgFormReuseTests.Fixed*`). The
  branch is gated on `!b.IsInline`.
- **Detach the fragmentainer for the deferred layout.** Nothing up an inline flow resumes a break token
  the child leaves, so a tall child lost the content after the break
  (`ResumableBlockLayoutIntegrationTests.TallOutOfFlowChildOfAnEngineContainer_KeepsAllOfItsContent`,
  `display:table`, where the child now sits in an anonymous cell's inline run). Laid out with the
  fragmentainer detached, as `LayoutEngineContent` lays out an engine's out-of-flow children. Probed
  against `main`: the child's lines land on the same pages either way (emission distributes them by
  position), so this is not a new gap. Real fragmentation of abspos boxes is #318.
- **Not in a multi-column container** (`DomParser.JoinsAsOutOfFlow`). The columns engine lays each child
  out as block-level column content. With the abspos child no longer forcing the anonymous-block wrap,
  `Some <b>bold</b> text<abs/>` gave "Some", "bold" and "text" a line each. Widening the columns
  dispatch the way #1038 did for floats does not help, because the wrap is what is missing. There an
  abspos child is still block-level, as before. (Floats have the same shape there already; not changed.)
- `display: none` abspos children are not set aside, matching `LayoutOutOfFlowChildren`: laying one
  out runs its prologue, which registers `string-set`.
- The intrinsic-width skip was forced by `IntrinsicWidthWalkTests.AnOutOfFlowOrUndisplayedSibling_…`:
  once the child sits in the same run as a float, walking into it ended the float's line.

**Not done:**
- **Static position (#1303):** it is still not implemented. With `auto` offsets a box sits at its
  containing block's padding corner, in block flow and inline flow alike. That predates this change.
- **Nested boxes (#1304):** a box nested in a float or inline-block inside a positioned inline is laid
  out before the inline's fragments exist, so it gets no inline containing block.

Both are recorded in `.claude/accepted-gaps/`.

**Evidence:** new `AbsolutelyPositionedInInlineContentTests` (line not broken, span not split,
left/top, right/bottom/percentage, left+right fill, auto block margins, wrapped inline first/last
fragments, `rtl`, tall child content kept, child on a discarded line laid out once, child's own content
normalized, both #1299 repros draw the ring, split pieces keep the outline). Full net8.0 suite green.
All 155 showcases rendered before and after and raster-diffed with PDFium: only `writing_mode.pdf`
page 8 changed. That is §10d's "position: absolute nested inside vertical line flow", where
"Before"/"after" now share one vertical line as its label describes. PDFium and MuPDF agree. New
`positioned_inline` showcase.
