# `text-align:center`/`justify` overflow guard, nowrap wrapping across sibling boxes, and vertical justify (#840, #841, #843)

## Load-bearing idea

Three bugs, fixed together since all three live in `CssLayoutEngine.cs`'s line/column-building and
alignment code. #840 and #841 were both filed as deliberate out-of-scope items during the #694
(`text-overflow: ellipsis`) work; #843 was filed (and then fixed in the same pass, once a code
review caught a real gap in the first attempt) as the vertical-writing-mode counterpart of #840.

**#840** - `ApplyCenterAlignment` had the exact same pre-#797/#694 one-directional guard
`ApplyRightAlignment` used to (`if (!(diff > 0)) return;`): fixed identically, to
`if (!(Math.Abs(diff) > 0)) return;`, so an overflowing centered nowrap line now spills
symmetrically past both edges instead of sitting untouched at its natural (flush-left) position.

`ApplyJustifyAlignment` turned out **not** to share that guard shape at all - it already
force-flushes its last word to the line's target edge unconditionally, which happens to
correctly handle the common case (a single unbreakable word alone on an overflowing line, since
it's simultaneously the line's first and last word). The actual bug, found only by constructing a
line with an overflowing *nested nowrap run of more than one word* (`<span
style="white-space:nowrap">two words</span>` inside a normally-wrapping justified block - a
realistic case, e.g. a "10 km" or a name that must not break), was that the unconditional
last-word override moved that word **backward past an earlier sibling word's own trailing edge**,
because the per-word `spacing` value (normally small, added between each pair of words) goes
hugely negative when an already-overflowing multi-word run has to be compressed to "fit" the
justify math. The result: two words rendered overlapping/garbled, not merely mispositioned.
Confirmed empirically (`CssLineBox.Words` dump) before writing any fix - both the single-word and
multi-word overflow shapes needed checking separately, since they don't share a failure mode.

Fixed with two changes to `ApplyJustifyAlignment`, not one: an overflowing line's per-word gap
floors at that specific pair's own natural `CssRect.ActualWordSpacing` (0 when the word has no
space after it) rather than a flat zero - a first attempt that floored the shared `spacing` value
at zero passed every test but was caught in review: `white-space: nowrap` forbids *breaking*
between two words, not collapsing their real space to nothing, so a flat-zero floor glued the two
words together with no visible gap at all, exactly as wrong as the overlap it was meant to fix.
Flooring at each word's own `ActualWordSpacing` keeps the same monotonic guarantee (no word's
`Left` is ever less than the previous word's `Right`, since the floor is never negative) while
still rendering the space the source actually has. Second: the last-word flush-to-edge override
now only fires when the line has exactly one word (nothing to overlap) or the line doesn't
actually overflow (the pre-existing, already-correct normal-justify path).
A multi-word overflowing line therefore keeps its natural, non-overlapping relative order and
spills coherently past the container's right edge - the same "actively align despite overflow,
don't silently leave it at natural position" philosophy #797/#694 already established for
`ApplyRightAlignment`, just expressed differently because justify's per-word redistribution can
overlap where center/right's single *uniform* shift never can (every word moves by the same
amount, so relative spacing between words is preserved regardless of shift sign).

**#841** - `CssLayoutEngine.FlowBox`'s `wrapNoWrapBox` mechanism exists to move a *nested*
`white-space:nowrap` run (e.g. `<span style="white-space:nowrap">`) to a fresh line as a whole
unit when it doesn't fit inline - legitimate when the containing block itself still wraps
normally elsewhere. The trigger condition only checked the run's own `b.WhiteSpace.Value ==
NoWrap`, never whether the *containing block* actually permits a line break at all - so when the
containing block was itself `white-space:nowrap`, this manufactured a second line box despite
`nowrap` forbidding wrapping altogether. Confirmed to reproduce identically with `<b>` instead of
`<span>` and with/without `overflow:hidden` (matching the issue's own repro notes) - purely a
wrap-boundary defect, unrelated to which element triggers it or to overflow/clip handling. Fixed
by gating the trigger on `blockBox.WhiteSpace.Value` also permitting wrap (not `NoWrap`/`Pre`) -
a narrower, block-level check rather than a fully general "does *any* ancestor between here and
the block permit wrapping" walk, since the reported repro (and the common real-world case: nowrap
inherited straight from the block) doesn't need the general form. A deeply nested
`white-space:normal` span re-enabling wrap inside an outer nowrap block is not handled by this
fix and remains exactly as unhandled as before - not newly broken, just not in scope.

**#843** - `ApplyVerticalJustifyAlignment`, the vertical-writing-mode counterpart of
`ApplyJustifyAlignment`, had the exact same shape #840 fixed horizontally: an unconditional
per-word `spacing` (no floor) and an unconditional last-word flush-to-edge override. Ported the
identical fix - each overflowing gap floors at that word's own natural `CssRect.ActualWordSpacing`,
and the last-word override only fires for a single-word or non-overflowing column - mechanically
translated from `Left`/`Right`/`Width` to `Top`/`Bottom`/`Height` (and both `InlineStartIsBottom`
cursor directions, since vertical has two instead of horizontal's one).

**A genuine end-to-end repro for the multi-word overflow case could not be constructed**, and this
is itself a real finding, not just a testing footnote: `CreateVerticalLineBoxes` (vertical column
line-breaking) has no counterpart to horizontal's `wrapNoWrapBox` (#841) - a nested
`white-space:nowrap` run of more than one word splits across separate columns instead of moving
together as a unit, confirmed empirically with the exact same fixture shape #840's horizontal test
uses. Without that grouping mechanism, normal column-breaking always finds a split point between
two words once nothing forces them to stay together, so "two-or-more words sharing one overflowing
non-last column" literally cannot happen via markup yet. Filed as
[#844](https://github.com/jhaygood86/PeachPDF/issues/844) rather than expanding this change's scope
to fix vertical `wrapNoWrapBox` too. The vertical fix is covered instead by a single-word-overflow
regression test (mirrors #840's own single-word carve-out test) plus the pre-existing non-overflow
multi-word vertical justify tests, which both continue to pass unchanged.

## What running it (not just reading it) confirmed

- Regression-guarded the *legitimate* `wrapNoWrapBox` scenario (nested nowrap span inside an
  otherwise-wrapping block) still moves the span to its own line as a unit, unchanged by the
  block-level guard - `NowrapMultiBoxWrappingTests.NestedNowrapSpan_InOtherwiseWrappingBlock_StillMovesToNextLineAsUnit`.
- A code-review pass caught a real defect in the first version of the #840 justify fix before it
  landed: flooring the shared `spacing` at a flat zero (rather than each pair's own natural
  `ActualWordSpacing`) stopped the overlap but glued the two nowrap-joined words together with no
  visible gap at all - passed every test that existed at the time, since none of them asserted on
  gap width, only on non-overlap. Caught only by an independent review reasoning through what
  `white-space: nowrap` actually forbids (breaking, not space-collapsing). Fixed, and the
  regression test tightened to assert a real (`> 1pt`) gap, not just non-overlap.
- `CenterJustifyOverflowAlignmentTests`/`NowrapMultiBoxWrappingTests` (8 tests) plus the new vertical
  single-word-overflow test in `VerticalTextAlignIntegrationTests` (1 test): all pass.
- Full `Rtl|TextAlign|WhiteSpace|Justify|Nowrap|TextOverflow|TextIndent`-filtered sweep and the full
  vertical text-align suite: all pass, no regressions in the adjacent alignment/wrapping code these
  fixes touch.
- Full suite (`dotnet test --framework net8.0`): run after the above, see PR for final count.
- Diff coverage (`diff-cover` against `main`): 100% on the changed lines.
