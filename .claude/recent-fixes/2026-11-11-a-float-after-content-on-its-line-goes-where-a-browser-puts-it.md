# A float after content on its line goes where a browser puts it (#1784, #2227)

Workstream C of the float issues plan, outside multi-column containers.

**The model, from reading Chromium, Gecko and WebKit rather than the spec's "reflowed".** None of them re-breaks the line: the float is tested once against the
width the line has used (Chromium `ShouldPushFloatAfterLine`, Gecko `BlockReflowState::AddFloat`, WebKit `shouldTryToPlaceFloatBox`). If the words so far fit beside it
the line narrows (here: the words move beside it, `ShiftLineBesideFloat`); if not, the float is deferred below the line and the text after it goes on along the line
it was on. `FlowFloatChild` did the first half and, for the second, closed the line and let the float open the next one, so every later word started a line.

**Changes in `CssLayoutEngine`.**
- A float that cannot be placed beside the words goes at the line's bottom (`PlaceFloatAtLineTop(..., MaxBottom)`, which the right-float-over-an-inline-block case already
  used) and the line is not closed.
- A line that cannot wrap (`white-space: nowrap`/`pre`) takes the same two paths instead of being skipped: it moves beside a float that fits, and is otherwise left alone
  (nothing opens a line after it, which was the old reason for skipping it).
- An inline-block with no block-level content is moved with the words (or by itself when it has none), counted in the extents `ShiftLineBesideFloat` measures and in
  `FloatOverlapsMovableAtomics`: a left float after one moves it right when it fits (#2227). An empty one had no word to move, so it stayed under the float.

**Measured, not assumed.** 294 generated documents (`<span>` per word, a float of three widths and two heights, left and right, after 3 to 20 words and before 3 to 25, with
and without nowrap, plus two left floats after one run) were laid out in Chromium (served over a local HTTP server, each word's `getBoundingClientRect`) and here, compared
word by word (3px across, 6px down, the first word's top as origin): 790 of 6,925 words differed from Chromium before, none after (58 documents better, none worse).
`FloatAfterTextMatchesBrowserTests` keeps nine of them with Chromium's numbers. The first run of the corpus gave a misleading baseline: words that were separate `<span>`s with
whitespace-only text nodes around the float lost the space between them, which hid the wrapping change. That is a separate defect (below); the corpus then put each word's space
inside its span, as running text has it.

**Found by running it.** A wrap that "did not happen" was `hasOrdinaryWrapBefore` false: the word after the float had no space before it and the word before it none after.
`LineBelowACrowdingFloat_WithNegativeMarginBottom_ClearsItsBorderBox` failed inside a column: a float placed below a line that goes on does not fit the column fill's
bookkeeping, so columns keep the old behavior (the line is closed and the float opens the next).

**Not done** (in `.claude/accepted-gaps/float-after-text-differs-from-a-browsers-line-layout.md`): multi-column containers, `shape-outside` (#1785), and the lost space around a float
between two whitespace-only text nodes. Block-level inline-blocks and `inline-table`/`-flex`/`-grid` still go below the line when a float overlaps them
(`.claude/accepted-gaps/a-float-after-an-inline-block-differs-in-some-shapes.md`).

**#2220** does not reproduce: the float table in a cell sits at the top of the cell's content box. `FloatInTableCellPlacementTests`, added with the table float-avoidance fixes (#2306), already covers it.

**Evidence.** Release engine suite 19,396, `PeachPDF.Tests` 1,807, zero warnings.
