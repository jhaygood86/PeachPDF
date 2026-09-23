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
- `DomUtils.InlineContainingBlockOf` builds the containing block from an inline's first and last
  `Rectangles` (border boxes, so borders come off; `rtl` swaps left/right). CSS 2.1 §10.1 (4.1) only
  defines the one-line case and leaves a wrapped inline undefined; the first/last-fragment corner rule
  and the `rtl` swap are CSS Positioned Layout 3 §2.1's, which Chromium follows with padding edges too.
- An inline holding only absolutely positioned boxes has no `Rectangles` at all. `FlowBox` records each
  set-aside box's place on the line (`SetAsideBox`: line, cursor X/Y, the word count then and the
  preceding word as anchor), `AnchorSetAsideBoxes` gives one with no preceding word the *following*
  word as anchor just before `FinalizeLineBoxes`, and `EmptyInlineContainingBlockFor` gives such an
  inline a zero-width containing block for the duration of the box's layout
  (`CssBox.EmptyInlineContainingBlock`). X follows the anchor through `FinalizeLineBoxes`
  (`PlaceOnFinalLine`): a translation for an even bidi level, a reflection within the word for an odd
  one, since `ApplyBidiReordering` reverses an rtl run within its span. The block extent is one font
  height from the line's `BaselineY` less ascent, or from the flow's Y on a line with no word, plus
  padding. The inline's own `vertical-align` is not applied (#1308, see below). Every site
  that read the positioned ancestor's `Location`/`ActualWidth`/`ActualHeight` consults it first:
  `CommitBlockChildOffset`, the `right`/`bottom` fallbacks, `ResolvePositionedAutoBlockMargins`, and
  `GetBoxWidth`/`GetBoxHeight` (`CssLayoutEngine.InlinePercentageBase`). Block ancestors are untouched.
- `InheritStyle(everything)` now copies the four `outline-*` longhands, so a genuine block-in-inline
  split (and a repeated header clone) keeps its outline.
- `GetMinMaxSumWords`, `GetMinimumWidth_LongestWord` and `GetLargestChildWidth` skip absolutely
  positioned children. `GetLargestChildWidth` is the fit-content half a float uses, and missing it left
  a nowrap badge widening its float (189.6pt against 24.9pt) on the branch and on `v0.9.19` alike.
- `ApplyCellVerticalAlignment` never measures an absolutely positioned child, and
  `GetMaximumBottom`/`GetMaximumRight` skip them further down (all three callers want in-flow content).
  It still *moves* one whose offsets on the alignment axis are both `auto`
  (`IsPlacedByItsContainingBlockAlong`): that box belongs at its static position, inside the aligned
  content. Static position is not computed (#1303), so moving it keeps the old, closer approximation;
  the first cut stopped moving every such child, which a code review caught as a regression from `main`.
- `PlaceOnFinalLine` reads the level `ApplyBidiReordering` actually used (`ReorderedLevelOf`): UAX #9 L1
  resets the line's trailing space words to the paragraph level only in the reorder's local array, so a
  preserved space between two rtl words that ends a line still stores an odd `BidiLevel`.
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
- **A repeated `<thead>` lost its text on every continuation page** (found in review, first cut only).
  The cause was not the proxy snapshot and not the deferred `LayoutBlockChild` as such, though
  neutering that call hid it: once laid out, the abs box nested in the `<th>`'s inline hung below the
  cell (`top: 100%`), `GetMaximumBottom` counted it, and the `<th>`'s default `vertical-align: middle`
  shifted the whole line ~7pt *up*. Page 0 still showed it; every repeat put it above its page's
  content band, where emission drops it. The proxy snapshots made that visible: the word's origin sat
  above its own cell's top on every proxy. Any middle-aligned cell with such a badge was shifted too,
  just not off the page. Before this change the box was hoisted, so the walk met it differently.
- **Anchor the empty inline's X to a word, never to the raw cursor.** The first cut kept the flow-time
  X when no word preceded it, so a badge at the start of a centred line (a `<th>` is centred by
  default) stayed at the cell's left edge; a line with no word read `LineTop`, which is 0 there
  (`FlowTop` is only set when a word is placed); and under `rtl` a plain shift put the anchor on the
  wrong side of the preceding word. All three came from the post-change review, not from tests.
  Two later code reviews found two more of the same kind. On a justified line, a place after a space
  moved only with the word before it while justification widened the space, so the following word is
  preferred whenever a space separates the preceding one (`AnchorSetAsideBoxes`). And a line with no
  word at all has nothing for alignment to move, so `PlaceOnWordlessLine` aligns the zero-width place
  itself by the line's used alignment and direction (`dir=rtl` → right edge, `center` → middle).
- **Only an inline in the flow being finalized is "empty".** Mid-flow, a positioned inline's
  `Rectangles` are reset, so a box nested in a float inside it (#1304's shape) also sees zero
  rectangles. Without the `IsSelfOrDescendantOf(ancestor, line.OwnerBox)` guard, it took the float's
  inner line for the inline's and anchored at an arbitrary point in the float, which the float's own
  placement then did not carry along (the box escapes its translation). The guard keeps that shape the
  tracked gap.
- The intrinsic-width skip was forced by `IntrinsicWidthWalkTests.AnOutOfFlowOrUndisplayedSibling_…`:
  once the child sits in the same run as a float, walking into it ended the float's line.

**Not done:**
- **Static position (#1303):** it is still not implemented. With `auto` offsets a box sits at its
  containing block's padding corner, in block flow and inline flow alike. That predates this change.
- **Nested boxes (#1304):** a box nested in a float or inline-block inside a positioned inline is laid
  out before the inline's fragments exist, so it gets no inline containing block.
- **An empty positioned inline's own `vertical-align` (#1308)** (`super`, `top`, a length) does not move its
  zero-width containing block, which always sits on the line's baseline. Rare, and the vertical-align
  pass only moves words and `Rectangles`, which such an inline has none of.

All three are recorded in `.claude/accepted-gaps/`.

**Evidence:** new `AbsolutelyPositionedInInlineContentTests` (line not broken, span not split,
left/top, right/bottom/percentage, left+right fill, auto block margins, wrapped inline first/last
fragments, `rtl`, tall child content kept, child on a discarded line laid out once, child's own content
normalized, both #1299 repros draw the ring, split pieces keep the outline). Review follow-ups added
the repeated-header text on every page (both review repros, asserted from the fragment tree), the empty
inline (between words under `left`/`center`, at the start of a `center`/`right` line, at line start,
on a wordless line, in an rtl run), percentage `left`/`top` and
`right`/`bottom` against an inline, a child on a completed line before a page break, the
`JoinsTheInlineRun` arm, the float width and both cell-alignment shapes. Each was mutation-checked:
reverting the site it covers fails it. Full net8.0 suite green.
All 155 showcases rendered before and after and raster-diffed with PDFium: only `writing_mode.pdf`
page 8 changed. That is §10d's "position: absolute nested inside vertical line flow", where
"Before"/"after" now share one vertical line as its label describes. PDFium and MuPDF agree. New
`positioned_inline` showcase, which the follow-up extended with badges in empty inlines. Re-rendered
all 156 showcases before and after the follow-up and raster-diffed with PDFium: only that showcase's
page changed, and PDFium and MuPDF agree on it.
