# A line is claimed by the page its line box is on, and a float moved whole lays out the same again

Two blockers from the third review round of the auto-height scroll container PR (#1334). Both were
pre-existing mechanisms the PR's new movers exposed; neither is specific to scroll containers.

## A heading drawn on no page

**Symptom.** An `overflow: hidden` card near a page foot, `h2 { font-size: 13pt; margin: 10pt 0 4pt }`
under `body { font: 10pt/12pt Arial }`: the card split, and "Card 2" was drawn on no page. The same card
with `overflow: visible` lost its heading on `v0.9.20` too; the PR only turned a shape that used to move
whole into one that splits.

**Mechanism, found by dumping the fragment tree.** The break moved the `h2` to the next page's top
(line box `FlowTop` = 180, the page boundary). A 13pt glyph on a 12pt line has a negative half-leading,
so the anonymous inline box's line rectangle, and its words, started at 178.5: 1.5pt *above* the boundary.
`FragmentEmitter.ClaimsLine` took the nominal slot from that rectangle's top, which named the page the
line had left. That page never reaches the `h2` (the block's own bounds, 180–192, do not overlap it), and
the page that does reach it rejected the line as nominally belonging elsewhere. The #1054 rescue does not
fire, because it is gated to a nominal slot *before* the sweep's `fromSlot` (the #1047 fix), and here both
slots are in the same sweep.

**Fix.** `ClaimsLine` takes the nominal slot from the line box's top where the box's ink rises above it
(`FragmentEmitter.InkRiseAboveLineTop`: `max(0, FlowTop - lineRect.Top)`, added to the already
shifted/displaced rectangle top). It only ever moves the nominal top *down*, so positive leading is
unchanged, and it is skipped for snapshot geometry (a repeated header's captured copy), where the live line
does not describe the copy.

That needed `FlowTop` to be true after a translation, which it was not: `CssBox.OffsetTop` moved words and
rectangles but left each line's `FlowTop`/`BaselineY` behind. Only `CssLayoutEngine.OffsetBoxWithinLine`
corrected them (`OffsetOwnLineBoxes`). `OffsetTop` now shifts its own line boxes
(`CssBox.OffsetOwnLineBoxesTop`), and `OffsetOwnLineBoxes` is gone, since keeping it would shift twice. The
review pass found one mover that moves a line's words without moving its owner: a table cell's vertical
alignment (`OffsetCellContent`), which moves the cell's children but not the cell that owns their lines. It
now shifts the cell's own lines too, and `TableRowCursor.Retract` undoes that through the same call. See
[the invariant](../invariants/fragmentation-a-translation-moves-a-lines-flowtop-with-its-words.md).

The #1054 rescue's "does the line fall past its band" test is asked of the same top as the nominal slot,
so the page the line left cannot claim it through the rescue either.

**Still true, not changed here:** the verdict is per box on a line, not per line. On a line that mixes a box
whose ink rises above the line with a smaller one that does not, the two can resolve different nominal
slots. That only matters where a line straddles a boundary (a sliced float or monolithic run), and was
equally true before.

**Rejected.** Descending into a block whose children's ink overlaps a page, so the page the line left
claims it: that draws the heading 1.5pt tall at the foot of the page it was moved off. Deriving the line top
from the inline box's rectangle and `line-height`: the rectangle includes the box's padding and border and
is propagated to inline parents, so the arithmetic is wrong for any padded span.

## A moved float drifts on every re-layout

**Symptom.** Laying the same tree out repeatedly (`LayoutHarness.LayoutRepeatedlyAsync`) moved the
anonymous inline box inside a float moved whole (`CssBox.MoveWholeOntoTheNextPageIfItFits`) down by the
move's distance on every pass (+52pt each time), while its words stayed put.

**Mechanism.** The inline flow never assigns a plain inline box's `Location`; its geometry is its
rectangles and words, which are reset per layout. The move's `OffsetTop` translated that `Location` as
well, so it accumulated one move per layout.

**Fix.** The flow resets a plain (non-atomic, in-flow) inline box's `Location` to the origin where it
already resets the box's rectangles (`ResumeOrdinal == 0` only, so a resumed pass keeps what earlier
fragmentainers set). Floats and atomic inlines are excluded: the flow does place those, and a float an
earlier pass placed keeps that geometry.

## Evidence

- `MovedFloatRelayoutIdempotencyTests` (the reviewer's `MovedFloat` and wrapper-beside-a-moved-float
  shapes, every box including anonymous ones, words included, four passes against a fresh layout) and
  `MonolithicContentLayoutIntegrationTests.AHeadingWhoseInkRisesAboveItsLine_MovedToThePageTop_IsDrawn`
  (painted strings, `hidden` and `visible`, each heading drawn exactly once) and
  `LineTopFollowsItsWordsTests` (a `middle`/`bottom` aligned cell's own line): all fail without the fix,
  pass with it.
- Full net8.0 suite: 15,162 passed, 0 failed. Diff coverage of the changed production lines: 100%.
- All 168 showcases: page content streams identical before and after (compared per page with PyMuPDF;
  the files themselves differ only in document metadata).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
