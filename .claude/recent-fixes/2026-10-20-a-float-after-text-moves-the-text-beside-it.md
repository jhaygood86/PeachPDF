# A float after text moves that text beside it, instead of overlapping it

**Load-bearing idea:** `CssLayoutEngine.FlowFloatChild` placed a float at the top of the current line and never moved the words already on that line. They stayed where they were, under the float. The float is now placed as before, and if it covers words already on its line, those words move right past it when they still fit on the line, or the float starts the next line when they do not. This is what a browser does, measured below. The float never lands on a word, except a float wider than its line (`FloatFitsLine`): there reopening the line moved the words after it past the container edge, so it keeps its place as on `main` ([accepted gap](../accepted-gaps/float-wider-than-its-line-after-text-keeps-its-place.md)).

**Multi-column containers:** `DomParser.CorrectInlineBoxesParent` now wraps a float together with the text around it, as it does in any block. The exclusion that kept a float out of the wrapped run put the float in the column flow, where nothing could move the text on its line beside it. See [the inline-only columnize fix](2026-10-20-inline-only-multicol-columnizes.md).

**Measured against Chrome, word positions, same HTML:**

| Case | Before | After |
|---|---|---|
| Plain block, 200px, 80x60 float after 30 words: overlapping words / mismatch | 6 / 42 | 0 / 0 |
| Plain block, 600px, 120x80 float after 60 words: overlapping words / mismatch | 4 / 129 | 0 / 0 |
| Plain block, float that does not fit beside its line: mismatch | 0 | 0 |
| Two columns, 600px, same float: overlapping words / mismatch | 4 / 4 (exclusion) | 0 / 96 |
| Two columns, 200px, 80x60 float: overlapping words / mismatch | 2 / 51 (exclusion) | 0 / 44 |
| Two columns, 600px, single column (`columns: 1`): overlapping words / mismatch | 4 / 129 | 0 / 0 |

The two-column 600px row is worse on word positions than the exclusion was. The words beside the float match Chrome row for row. The difference is which words fall in column 1: balancing does not count the space the float takes, which `html-css-support.md` already lists as a limitation, and Chrome's balance does count it.

- A float at the start of its line, or after text that already ends before it, is unchanged.
- All 221 showcases have the same word layout as `HEAD`.
- `XY <float>ZZZZ</float> more words` at 16px monospace: `more` now starts 0.3pt from Chrome's position. The old overlap put `XY` under the float.

**Tests that encoded the overlap, now asserting the new layout:**

- `IntrinsicWidthWalkTests.AFloatBesideInlineContent_HasRoomReservedForItByTheLine` required the box to be the sum of the text and the float. That only held because the text overlapped the float, so its right edge did not include the float's width. It now requires the box to reach the text's right edge beside the float, with no word over the float.
- `FloatLayoutRegressionTests.FloatWithLeftInsetAmidInlineContent_DoesNotLeakItsOwnInsetIntoTheNextSiblingsCursor` required `more` to start at the float's right edge, which put `XY` under the float. It now requires `XY` beside the float and `more` one space after `XY`.
- `MulticolLayoutIntegrationTests.InlineTextBesideAMiddleFloat_StartsPastIt` now also checks that no word is drawn over the float.

**Deliberately not done:**

- Re-laying the whole line with the float first, which is Chrome's method. Words are shifted, and the float moves down when a shift would need a re-break. That can differ from Chrome when the re-break matters.
- Counting a float's height in column balancing (documented as a limitation).
- The column word skip below.

**Column word skip (found while checking this, not fixed):** in a fixed-height multi-column box whose content is taller than its columns, column two can start a line late, and that line is lost. `CssLayoutEngineColumns.LayoutRunSegment` targets a column band taller than the box (the balanced target is not capped by the box's definite height). The line at the box's bottom is consumed by column one but lies past the box edge, so it is never emitted. Capping the band at the box height removes the loss at 150px, but the overflow then goes to a second page that holds no words, so the words are lost silently in a different place. The cap was reverted. A real fix needs overflow columns (css-multicol-1 §3.3) or an explicit overflow policy for a continuation.

**Evidence:**

- Full net8.0 suite with coverage: 17,172 passed, 9 skipped, 0 failed.
- diff-cover against `main` at the 90% gate: 100% of 53 changed executable lines.
- Solution `Rebuild`: 0 compiler warnings (the same 12 NuGet advisories as before).
- All 221 showcase PDFs: identical word layout to `HEAD`.
- New tests: `FloatAfterTextThatFitsBeside_MovesThatTextBesideIt`, `FloatAfterTextThatDoesNotFitBeside_StartsTheNextLine`, and `RightFloatAfterTextThatOverlapsIt_MovesThatTextLeftBesideIt`.
