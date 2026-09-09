# A block-content list item's `::marker` can land in the wrong column inside a multi-column container

`<li><p>…</p></li>` is numbered correctly on the page grid, but inside a **multi-column** container its
marker can still land in a different column fragment than the one the item begins in.
[CSS 2.1 §12.5.1](https://www.w3.org/TR/CSS21/generate.html#lists) and
[CSS Lists Level 3 §3.1](https://www.w3.org/TR/css-lists-3/#marker-position) put the marker beside the
item's **first** line box, so this is a real deviation. Tracked as
[#483](https://github.com/jhaygood86/PeachPDF/issues/483).

**Fixed:** the marker being claimed *twice* (once by a phantom, empty fragment in a column an abandoned
attempt positioned it in, once by its real fragment) or **zero** times. Root cause and fix below.
**Still open, narrower now:** a marker's one surviving fragment can still land in a column other than
the one the item's own first fragment is in, for an item spanning three or more fragments across both
page and column boundaries.

## What was fixed

**Root cause, traced from one sweep failure (`column-count:2;column-fill:auto`, 3 items, one `<p>` child
each, 300×120pt page):** `FragmentEmitter.BuildDraft` decides whether a box belongs to a slot by its own
per-line `Rectangles` where it has them, and falls back to its own captured `Location`/`ActualBottom`
bounds (`UsesOwnBounds`/`ownBoundsCoverRegion`) where it doesn't — an outside `::marker`'s case, since
`CssBox.LayoutOutsideMarker` positions it directly rather than through the ordinary inline flow that
assigns `Rectangles`. Those bounds are captured *unconditionally* by
`Fragments.BoxGeometrySnapshot.CaptureBox`, regardless of whether the marker's own word is
`AwaitsTheNextFragmentainer` in that snapshot. A column-fill attempt positions the marker in column A,
then — via the same one-shot early-break retry every box gets, not the "kept nothing" take-back alone —
discovers the item keeps nothing there after all and resumes the whole item in column B
(`CssBox.ResumeInTheNextFragmentainer`, `CssBox.TakeBackTheMarkerOfAnItemThisPassKeptNothingOf`). Column
A's stale bounds still register as "the marker is here," producing a second, **empty** `BoxFragment`
(zero `Words`, since the per-word claim loop upstream of `UsesOwnBounds` correctly excludes the word
there) alongside the real one in column B — invisible to a per-*word* claimed-once check, since the
phantom fragment carries no word to double-count.

**Fix:** narrow the bounds-based fallback (`ownBoundsCoverRegion` in `BuildDraft`) to exclude a box that
is `CssBox.IsOutsideMarker` **and** has a word **and** whose own item is a genuine
`display: list-item` box — not every word-bearing, rectangle-less box, and not a marker with no word at
all. Both narrowings were measured, not assumed:

- Restricting only to "no words" (any box, not just markers) still needed the per-word loop's own
  `AwaitsTheNextFragmentainer` check to answer membership for that box — but removing the bounds
  fallback for *every* such box (not just markers) regressed `Acid2RegressionTests.FullFixture_MatchesPrinceXmlPageCount`
  (2 pages → 1) and `FixedBars_RepeatOntoPage2_KnownResidualNotCoveredByIntro`: an ordinary inline box in
  that same "words, no rectangles" shape (bare text `CssLineBox.UpdateRectangle`'s
  `clonesDecorations`/`IsImage` gate skips) still legitimately needs its own bounds when none of its
  words are claimed in a slot its subtree otherwise belongs to.
- Narrowing to markers specifically (`IsOutsideMarker(box) && Words.Count > 0`) *still* regressed the
  same two Acid2 tests: Acid2's own fixture retargets three `<li>`s to `display: table-cell`/`table`
  without a `list-style: none` override (`ul li.first-part`/`.second-part`/`.third-part` in
  `PeachPDF.Tests/TestSupport/acid2.html`), and PeachPDF still produces a marker box with a real word for
  them even though CSS 2.1 §12.5.1 generates a marker only for `display: list-item` — a pre-existing,
  out-of-scope quirk this fix must not touch. Adding the `ParentBox?.DerivedStyle.ActualDisplay ==
  Keywords.ListItem` condition on top scopes the fix to genuine list items only, and both Acid2 tests
  pass again.

`UsesOwnBounds` itself stays `true` regardless of the new exclusion — it still governs `RectOf`/`ExtentOf`
sizing the marker's *real* fragment once its word has genuinely been claimed somewhere; only the
membership question (`ownBoundsCoverRegion`, "is this box's bounds alone enough to say it's here with no
claimed content") is narrowed.

**Measured effect** (216-combination sweep: `column-fill: auto|balance` × `column-count: 2|3` ×
`item-count: 3|5|8` × `child-count: 1|2|4` × `page-height: 120|200|300` × plain-text or block-only
children): markers claimed zero or twice dropped from **39 to 0**. A separate, narrower "claimed exactly
once but in the wrong column" count rose from 9 to 16 — expected, not a regression: several of those 16
are markers that were previously claimed *zero* times (invisible, #444's own symptom) and are now claimed
once, just not always in the column matching the item's first fragment yet. See below for that remainder.

**Regression test:** `StraddlingListMarkerTests.ABlockContentItemAColumnFillAttemptAbandons_ClaimsItsMarkerExactlyOnce`
— confirmed to fail against pre-fix code (`Assert.Single` finding 2 marker fragments) and pass post-fix.

## What is still open

An item spanning **three or more** fragments across both a page boundary and a column boundary in the
same run (measured on `column-count:2;column-fill:auto`, 5 items, four empty `<div>` children each,
120pt page: the item's own fragments land at page 0/column 1 (a sliver), page 0/column 2, and page
1/column 2) can still end up with its marker's one surviving fragment in none of those three columns —
a single, valid claim, just not demonstrably the item's *first* one. This is the "at least one further
whole-container relayout attempt" the previous investigation flagged as untraced; it remains untraced.
Closing it fully still means settling how the columns engine fragments a block-level list item across
*that* many fragmentainer boundaries, which is a larger change than this fix's narrow `FragmentEmitter`
membership correction.

## History (superseded findings, kept for context)

Two earlier investigation passes are folded into the above rather than kept as separate log entries:

- A first attempt found the mechanism was not `CssBox.LayoutBlockChildren`'s column-rejection arms
  (§3.1 forced/avoid-column-break, column-overflow, column-span:all, the orphans retry) — a take-back
  mirroring `TakeBackTheMarkerOfAnItemThisPassKeptNothingOf` at all four arms measured bit-for-bit
  identical late/bad counts with and without it, because every one of those arms hands the rejected
  child a fresh `BlockBreakToken` with `ChildToken: null`, and `MarkerBelongsToTheFragmentainerBeingFilled`
  already treats a `null` resume as "reposition it" regardless of whether `AwaitPlacement` ran.
- A second attempt correctly identified `CssBox.ResumeInTheNextFragmentainer` as the seam (only the
  box's own `Location` moves, not the marker's word) but had not yet traced *which* mechanism leaves the
  marker's bounds stale in the fragment tree specifically — that is `FragmentEmitter.BuildDraft`'s
  `UsesOwnBounds` fallback, identified and fixed above.
