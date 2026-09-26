# A line is claimed by the page its line box is on, not by where its ink starts

Found in the third review round of #1334 (auto-height scroll containers), and split out of it because it
is a `main` bug on its own: the `overflow: visible` form loses its heading on `v0.9.20`.

## A heading drawn on no page

**Symptom.** A bordered, padded card near a page foot, `h2 { font-size: 13pt; margin: 10pt 0 4pt }`
under `body { font: 10pt/12pt Arial }`: the card split, and its heading was drawn on no page.

**Mechanism, found by dumping the fragment tree.**
- The break moved the `h2` to the next page's top (line box `FlowTop` = 180, the page boundary).
- A 13pt glyph on a 12pt line has a negative half-leading. So the anonymous inline box's line rectangle,
  and its words, started at 178.5: 1.5pt *above* the boundary.
- `FragmentEmitter.ClaimsLine` took the nominal slot from that rectangle's top, which named the page the
  line had left.
- That page never reaches the `h2`: the block's own bounds, 180–192, do not overlap it. The page that does
  reach it rejected the line as nominally belonging elsewhere.
- The #1054 rescue does not fire. It is gated to a nominal slot *before* the sweep's `fromSlot` (the #1047
  fix), and here both slots are in the same sweep.

**Fix.** `ClaimsLine` takes the nominal slot from the line box's top wherever the box's ink rises above it
(`FragmentEmitter.InkRiseAboveLineTop`, which adds `max(0, FlowTop - lineRect.Top)` to the rectangle top
after it has already been shifted/displaced).
- It only ever moves the nominal top *down*, so it changes only lines whose ink starts above their line
  box. Positive leading alone never does that, but the next section's inline-block words do.
- It is skipped for snapshot geometry (a repeated header's captured copy), where the live line does not
  describe the copy.
- The #1054 rescue's "does the line fall past its band" test is asked of the same top, so the page the line
  left cannot claim it through the rescue either.

**Keeping `FlowTop` true after a move.** The fix needs `FlowTop` to be correct after a translation, and it
was not:
- `CssBox.OffsetTop` moved words and rectangles but left each line's `FlowTop`/`BaselineY` behind. Only
  `CssLayoutEngine.OffsetBoxWithinLine` corrected them (`OffsetOwnLineBoxes`).
- `OffsetTop` now shifts the lines of each box it moves (`CssBox.OffsetOwnLineBoxesTop`).
  `OffsetOwnLineBoxes` is gone, since keeping it would shift twice.
- One mover moves a line's words without moving its owner: a table cell's vertical alignment
  (`CssLayoutEngine.OffsetCellContent`). It moves the cell's children but not the cell, which owns their
  line. It now shifts the cell's own lines too, and `TableRowCursor.Retract` undoes that through the same
  call.

See [the invariant](../invariants/fragmentation-a-translation-moves-a-lines-flowtop-with-its-words.md).

**A stale line box, exposed by keeping `FlowTop` true.** An adversarial review's fuzz found one document that
lost a word on this branch while `main` and v0.9.20 drew it (seed 226, a grid item).
- **What was stale.** An inline-block whose text the surrounding line has taken still holds a line box from an
  earlier sizing layout. That line box still lists the text, but every word's `Line` now points at the outer
  line.
- **Why `main` survived it.** `LastOwnLineBaselineOf` read that stale line's `BaselineY` as the inline-block's
  baseline. On `main` the value was never translated, and by chance it sat where the next vertical-alignment
  pass expected, so that pass moved nothing.
- **What broke here.** With translations now moving line boxes, the grid's −109.2pt item translation moved the
  stale line too. The commit pass's alignment then read a baseline 214.5pt off and moved the inline-block by
  +107.25pt instead of 0, onto no page.
- **The fix.** `LastOwnLineBaselineOf` skips a line whose words all live on another line.
- **Tests.**
  - `AnInlineBlockInAnEngineItem_IsDrawnOnceAtItsLine` uses the review's minimized document verbatim; a
    simplified copy no longer reached the stale line.
  - `AnInlineBlockMovedToTheBaseline_KeepsItsOwnLineTopWithItsWords` fails if `OffsetBoxWithinLine` shifts the
    moved box's lines a second time, which the suite did not catch before (the review's mutant M7).

**Words above their own line top, and a line rejected by both pages.** jhaygood86's review of the first
commit found a padded `vertical-align: top` inline-block across a page foot losing whole lines: 48 of 59
words with `padding: 30pt`, 54 with `12pt`, where `main` and Chrome draw all 59.
- **The older bug underneath.** `EffectiveVerticalAlignOf` walks from an anonymous text box up to the nearest
  element to read its `vertical-align`. On the inline-block's own inner lines that element is the
  inline-block itself, the line's owner, whose `top` says how it sits in its parent's line. Its text was
  aligned to the top of its padded rectangle, over the padding and above the line top the flow recorded (by
  exactly the padding).
- **Why it lost lines here.** The ink was on page k and the line top on page k+1, so page k rejected the line
  as nominally page k+1's, and page k+1 rejected it because none of its ink reached it.
- **Fixing the older bug (the owner's `vertical-align` not read on its own lines) was tried and measured.** It
  puts the words where Chrome does (y=50.3 against Chrome's 50.2), but it also moves the lines off the page
  boundary they happened to sit on. Lines of a tall inline-block, which is laid out in one piece and sliced,
  then straddled slice boundaries and were lost (#1328's shape): 53 and 54 of 59 through the CLI, a new
  loss. So it is left to its own change, together with that slicing.
- **The fix here** is the review's second option. `ClaimsLine` trusts the line top only while the ink reaches
  the page it names, and otherwise falls back to the ink's page, so no line can be rejected by both pages.
  - Through the CLI, `main`, the first commit and this one draw 59/59, 48/59 and 59/59 at 30pt padding.
  - A 1728-document sweep (four `vertical-align` values × eight paddings × three top borders × 18 page
    heights) draws every word exactly once.
  - `APaddedTopAlignedInlineBlockAcrossAPageFoot_DrawsEveryWordOnce` fails without it.
- **Retract.** `UndoingACellsAlignment_TakesItsOwnLineTopBack` covers `OffsetCellContent` with a negative
  distance, which the suite missed (a mutant skipping the shift for distances of 0 or less survived).

**Test fonts, and a mutant left untested.**
- **Fonts.** The review's grid document triggers the stale line only with Arial's widths. Neither the bundled
  Source Sans 3 nor a sweep of page height (120–200pt) and item width (30–100%) reached it. So it stays in Arial
  as the end-to-end regression, and
  `ALineWhoseWordsLiveOnAnotherLine_IsNotAnInlineBlocksBaseline` tests the rule directly by making the stale
  state by hand. The heading and cell tests pin the bundled font, and still fail without the change they guard.
- **Mutant M4 survives.** Asking the #1054 rescue's "falls past its band" question of `rect.Top` instead of the
  line top is not caught by the suite. The two differ only when all of these hold:
  - a line's ink rises across a boundary it sits exactly on;
  - its slot is re-emitted in isolation (`_currentPassFromSlot` null: `CatchUpStaleSlotsBehind`, `Finish`'s
    replay);
  - a later slot asks about it.

  The line top then keeps that later slot from claiming the line a second time. A fixture for it needs a
  relocated box and a negative-leading line landing exactly on the boundary, with the font sensitivity above,
  so it is left as a known survivor rather than a brittle test.

**Still true, not changed here:** the verdict is made per box on a line, not per line. On a line that mixes
a box whose ink rises above the line with a smaller one that does not, the two can resolve different
nominal slots. That only matters where a line straddles a boundary (a sliced float or monolithic run), and
it was equally true before.

**Rejected.**
- Descending into a block whose children's ink overlaps a page, so that the page the line left claims it:
  that draws a 1.5pt sliver of the heading at the foot of the page it was moved off.
- Deriving the line top from the inline box's rectangle and `line-height`: the rectangle includes the box's
  padding and border and is propagated to inline parents, so the arithmetic is wrong for any padded span.

## Evidence

- `LineTopFollowsItsWordsTests` has two tests, and both fail without the fix:
  - the card, where each heading is painted exactly once;
  - a `middle`/`bottom` aligned cell's own line.
- The full net8.0 suite passes. Diff coverage of the changed production lines is 100%.
- 167 of the 168 showcases have identical page content streams before and after. The exception is `flexbox`
  page 2, which no longer draws one glyph of a boundary line that the page clip already hid. That page is
  pixel-identical in PDFium and MuPDF.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
