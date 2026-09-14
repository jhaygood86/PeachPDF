# A formatting-context root contains its floats

The third defect found by rendering Acid2 against its reference image (after
[line-height](2026-09-14-a-line-box-is-its-line-height-not-its-tallest-glyph-box.md) and
[block-end margin collapsing](2026-09-14-a-collapsed-block-end-margin-escapes-or-is-contained-never-dropped.md)).
The face's second row — `blockquote.first.one`, a shrink-wrapped `position: absolute` box whose only
content is one 48px float between 2em black side borders — was **invisible**, and also twice as wide as
it should have been. Two independent defects in the same box.

## Height: nobody contained anything

CSS 2.1 [§10.6.7](https://www.w3.org/TR/CSS21/visudet.html#root-height): a box that establishes a
formatting context of its own and takes its height from content grows to cover any floating descendant
whose bottom margin edge falls below its bottom content edge. That rule is the *entire* reason the
`overflow: hidden` containment idiom works, and why a float, an inline-block, an absolutely-positioned
box and a flex/grid item all contain their floats while an ordinary block does not.

It was not implemented at all — for any box kind. A formatting-context root holding nothing but a float
came out zero-height, so the blockquote's side borders had no height to be drawn over. Measured against
Chrome, every one of `position: absolute`, `float`, `overflow: hidden`, `overflow: auto` and
`inline-block` gave 0 where Chrome gives 12; the ordinary-block control correctly gave 0 in both.

The engine already had the predicate this needs — `DomUtils.EstablishesIndependentFormattingContext`,
written for the float *scan*'s boundary check — so the fix is a new
`DomUtils.LowestFloatBottomInOwnFormattingContext` walk plus one `Math.Max` in
`CssLayoutEngine.ApplyHeight`, gated on that predicate and on the height not being definite.

Three things the gate has to get right, each pinned by its own test:

- **`!isDefiniteHeight`, not `height == auto`.** An indefinite percentage height is automatic too — the
  same reading `isRatioHeight` beside it already relies on.
- **A definite height still wins** (§10.6.3): the float overflows it rather than growing it.
- **The walk stops at a nested formatting-context root**, whose own floats it already contains; its box
  bottom is ordinary content the caller's content height has counted. It also skips out-of-flow
  descendants (§9.3.1) and does not descend into a float, which is such a root itself.

## Width: the abs branch counted its own border twice

Separately, `GetBoxWidth`'s `position: absolute` shrink-to-fit branch returned
`GetFitContentWidth(...)` directly. `GetMinMaxWidth` returns an **outer** width — its result already has
the box's border and padding folded in (`maxWidth = paddingSum + ...`) — while what that branch returns
is a **content** width the caller adds them back onto. So the border was counted twice: 96px measured as
144px.

The float branch three lines above had this right all along, with the reasoning in its own comment
("Both are outer widths … and the box's own decoration comes back off at the end"). §10.3.7's
shrink-to-fit is §10.3.5's formula verbatim, so the abs branch is now literally that branch: the
min-content floor it was also missing, and the `- box.ActualBoxSizeIncludedWidth` subtraction.
`AbsolutelyPositionedBox_ShrinkToFitWidth_MatchesAFloatsOnTheSameContent` asserts the two against *each
other* on identical content, so neither can drift alone.

## What running it found

Two of the cases in the first draft of the test file were wrong, both because I wrote the expectation
instead of measuring it:

- `display: flow-root` was in the formatting-context-root theory. It fails — and correctly, because
  `flow-root` is **not implemented in this engine at all** (no occurrence anywhere in the source, and
  absent from `docs/html-css-support.md`). It computes as `block`, so it contains nothing. Asserting it
  would have pinned an unrelated missing feature to this fix. Dropped, with the reason in the theory's
  own comment, and the gap is now stated in the `float` row of the support matrix.
- The "float shorter than the line beside it" case asserted 14pt, a number carried over from a Chrome
  probe written in `px` against a fixture written in `pt`. It now asserts against the *same box measured
  without the float*, which says exactly "the float changed nothing here" and cannot drift with the
  machine's fallback font at all.

## Evidence

- New `FloatContainmentTests` (13 tests), every expectation measured in headless Chrome first. Against
  the unfixed code **10 of the 13 fail**; the three that pass are the ordinary-block control, the
  definite-height case and the shorter-float case — i.e. exactly the guards.
- Full suite: 11,373 passed, 0 failed, 9 skipped.
- `dotnet build PeachPDF.slnx -t:Rebuild` — 0 warnings.
- Diff coverage 98% (gate is 90%). The two lines reported missing are a pre-existing left-float
  mid-line branch (`CssLayoutEngine.cs`'s `lastLeftIntersectingFloatBox is not null` inside the word
  loop) that the suite has never exercised and that is not in this change's diff at all — `git diff`
  puts no hunk there; diff-cover attributes it to a neighbouring insertion.
- **Acid2: rows matching the reference went 72/168 → 84/168, and the face's second row now matches the
  reference exactly.** Across the three fixes so far: 48 → 72 → 84. The face stays 144 × 144. MuPDF and
  PDFium agree to 52px of 28,224.

## Still out of scope

`display: flow-root` (above). The remaining Acid2 differences are the missing HTML5 stray-`</p>`
element and the two documented Acid2 gaps.
