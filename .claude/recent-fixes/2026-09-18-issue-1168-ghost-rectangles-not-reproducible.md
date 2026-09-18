# #1168 (wrapping inline-block laid out twice, ghost rectangles) is not reproducible on current main

Investigated as its own PR in a batch of 18 post-0.9.18 issue fixes. The issue theorized
`CssLayoutEngine.LaysOutAsAnAtomicBox` could mistakenly report "fits on one line" for an inline-block
whose content needs to wrap, causing the box's content to be flattened word-by-word into its parent's
own line boxes before the engine "notices" and redoes it correctly via `FlowAtomicBlockContentChild` -
leaving the first attempt's `CssBox.Rectangles` entries stale and painted as a ghost duplicate.

## What was found by running it, not by reading it

The issue's own exact repro (the `atomic_inline_width` showcase's `wrapbox` markup, with `width:120pt`
moved from a stylesheet rule into an inline `style` attribute) produces exactly one `Rectangles` entry
on current `main` (`387048e8`) - no ghost. So does the same markup with the width left in a stylesheet
rule. Both take the atomic path (`FlowAtomicBlockContentChild`) on the very first evaluation;
`LaysOutAsAnAtomicBox` never returns the wrong verdict for either.

Pushed further: constructed a scenario with a *genuine* verdict flip within one layout generation - a
flex-grow item whose inline-block child has a `width: 30%`. The item's hypothetical auto-width
measurement pass (`CssLayoutEngineFlex`'s `MeasureItem`) sees the item at close to the flex container's
full width, so 30% of it comfortably fits the inline-block's content on one line
(`LaysOutAsAnAtomicBox` returns `false`, flatten path). The item's real, flex-resolved final width is
much narrower, so the same 30% no longer fits (`LaysOutAsAnAtomicBox` returns `true`, atomic path).
Confirmed via temporary instrumentation (not kept) that the verdict does flip `false` -> `true` across
these two passes - the exact mechanism the issue described. Even here, `Rectangles` ends up with
exactly one entry and no descendant carries a stale rectangle, in both the direct-child and
one-level-deeper-nested (`<p>` between the flex item and the inline-block) shapes. Also tried
`flex-direction: column` with stretch, a `column-fill: balance` retry, and a widows-forced page-break
rewind - all clean.

**Why the flip doesn't leave a ghost even where it demonstrably occurs**: every real re-visit of a box
inside `CssLayoutEngine.FlowBox` re-runs `if (coordinates.ResumeOrdinal == 0) b.RectanglesReset()`
before re-flowing that box's content (`CssLayoutEngine.cs` ~L3824) - and because the same guard fires
again for every descendant the recursive re-flow visits, a flip's whole abandoned subtree gets reset,
not just the top box. `CssBox.RectanglesReset()` itself is not recursive (confirmed by reading it,
`CssBox.cs` ~L8960-8967 - it only clears `this.Rectangles`), but the *call site* effectively is,
because re-flowing a box's content necessarily re-visits every child through the same dispatch that
holds the reset.

## Likely why the issue no longer reproduces

Two fixes landed in the same area shortly before this investigation, in the PR fixing #1032/#1105 (see
`2026-09-17-atomic-inline-isolated-measurement-and-line-wrap.md`):

- **#1032** fixed `CssBox.GetMinMaxWidth`'s intrinsic max-content-width walk, which
  `LaysOutAsAnAtomicBox` depends on directly (`GetMaxContentWidth(g, child) > available`). Before that
  fix, a block-level descendant inside an atomic inline-level box could make this walk report a wrong
  (typically too-small) max-content width - exactly the kind of bad input that would make the
  atomic-vs-flatten verdict wrong on a first pass.
- **#1105** gave three of the four atomic inline-level displays (`inline-table`, `inline-grid`,
  `inline-flex`) a preflight-and-wrap check they previously lacked entirely, and replaced a first,
  buggy fit-check-width estimate (`GetAtomicInlineFitCheckWidth`) with the same
  `ResolveAtomicInlineBlockWidth` the box's real layout uses - removing a second source of
  disagreement between what the fit check assumed and what layout actually did.

Both are plausible root causes for a wrong first-pass verdict of the kind #1168 describes, and both
were already fixed by the time this investigation started. No code change was made in this PR.

## What was deliberately not done

- No production code change - there is nothing to fix that reproduces.
- No accepted-gap file added - this isn't a gap being left in deliberately, it's a defect that turned
  out to already be closed.
- No migration-notes entry - this PR changes no behavior; the behavior change (if any) already shipped
  with #1032/#1105 and is covered by that PR's own migration note.

## Evidence

`src/PeachPDF.Tests/Integration/Issue1168GhostRectangleRegressionTests.cs`: 6 tests, all green -
the issue's own inline-`style` repro, the stylesheet-rule equivalent, the confirmed flex verdict-flip
(direct-child and nested-in-`<p>` shapes), a widows page-break rewind, and a mutation test that
manually injects a stale `Rectangles` entry (a removed-from-`LineBoxes` `CssLineBox`) and asserts the
detector actually reports it - proving the other five tests' `Assert.Empty(FindGhostRectangles(...))`
calls aren't passing vacuously against a broken detector. The five scenario tests assert both
`Rectangles.Count` and the whole-subtree "no stale line-keyed rectangle" scan
(`FindGhostRectangles`, checking every box's `Rectangles` keys are still present in their owning line's
own `LineBoxes`). Full net8.0 suite green (12,381 passed, 9 pre-existing platform skips, 0 failures).
Solution rebuild (`dotnet build PeachPDF.slnx -t:Rebuild`) 0 warnings. No production code changed, so
diff coverage is not applicable.
