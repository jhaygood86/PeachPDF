# A flex container and its items follow each page's measure (#196)

A block-level flex container that continues across pages of different widths (`@page :first { margin-left: 0 }`,
a per-page `size`) now sizes each fragment to its page, per css-break-3 §5.1: the container frame, a wrapping
row's later lines, and the items of a line that continues onto the page. See
[per-page-horizontal-reflow-scope.md](../accepted-gaps/per-page-horizontal-reflow-scope.md) for the mechanism
and what is left.

**The load-bearing idea.** Layout *states* a frame per slot instead of the emitter deriving it. A flex box's
frame is the product of the whole flex algorithm, which nothing outside the engine can re-run, and a box has
one `Location`/`Size` for all its fragments. So a statement is `(slot → absolute X, width)`, held on the
emitter (`_inlineFrames`), and read **at materialization** (`ExtentOf`) — *not* when the draft is built. The
first version read it in `BuildDraft` and item frames came out wrong: the pass that resumes into slot 1 states
slot 0's frame after slot 0's draft was frozen. (The container frame worked by accident, because the fresh
pass states it before any draft exists.)

**Found by running it rather than reading it.**

- Prince and Chromium disagree, and Prince is the better one. On every fixture, Chromium re-sizes only the
  container (items overflow at their first page's width); Prince also re-collects a wrapping row's later lines
  and re-fits items that straddle. Both keep a *column* container's items at their starting cross size, so that
  is not attempted here.
- A flex item's block-level children are not fragmented by break tokens - blocks are placed in one tall column
  and cut by the page edge - so there is no resume pass to re-fit a straddling *item with only block children*.
  Those get frames stated from the fresh pass (`StateSpannedLineFrames`). Items with text do resume.
- `ActualBottom` of an unfinished item at resume time is provisional (its top), so the line bottom a re-fit line
  is measured against has to be captured by the fresh pass (`FlexBreakToken.LineBottoms`). A first attempt read
  it off the boxes and computed a growth of a whole document height.
- After a re-fit, earlier slots keep the frame they had: `MoveFrame` states the old absolute frame for every
  earlier slot **before** moving live geometry, and later slots' statements from the fresh pass are kept (they
  hold for pages no pass visits).
- The stale pin is real and visible without any per-page variance: a flex container inside a table cell is laid
  out several times in one generation. `flexbox.pdf` page 3 (§8 order, §11) rasterizes differently from
  `main` - items are narrower, closer to Chromium's. Every other showcase (186 of 188 rasterized at 50dpi;
  `box_shadow` differs only at 50dpi, identically at 80) is unchanged.

**Not done, and why.** See the accepted-gap file: a later unstarted line pushed onto a page of a different
measure by a re-fitted line's growth (it would need the whole line re-collected, membership included); a
non-stretch item's offset after growth; flex inside multi-column; grid.

**Evidence.** `FlexPerPageReflowIntegrationTests` (13 tests, fragment-tree assertions); the full `PeachPDF.Tests`
suite green in Release on net8.0; `paged_media_flex_per_page_reflow` showcase rasterized through both PDFium and
MuPDF, identical to the eye. Chromium and Prince oracle scripts compared per-page frame X/width on 13 fixtures.
