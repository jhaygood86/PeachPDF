# target-counter()/target-text()/leader() (#703): two multi-pass layout staleness bugs found by building the feature, not by reading the code

Implementing css-content-3's `target-counter()`, `target-text()`, and `leader()` (issue #703) surfaced
two real, pre-existing-shaped bugs in how this codebase already does multi-pass layout - both only
showed up once a *third* consumer of "re-resolve content, then re-layout" existed alongside
`UseVariablePageWidth`'s reflow loop and `HtmlContainerInt.ReapplyPseudoElementContent`'s `string()`
re-application.

## Bug 1: `HtmlContainerInt.Root` is null during the very pass that matters most

`DomParser.GenerateCssTree` builds the box tree locally and only returns it to `HtmlContainerInt.SetHtml`,
which assigns `Root` *after* the whole method returns. `DomParser.CorrectTextBoxes` (which calls
`CssContentEngine.ApplyContent` for every box, including the first, DOM-construction-time content
resolution) runs *inside* `GenerateCssTree`, so any code resolving an id against `container.Root` at that
point always gets nothing back. `HtmlContainerInt.GetBoxById` originally did exactly this. Fixed by walking
from the box actually being resolved up to its own topmost ancestor via `ParentBox`, rather than trusting
`Root` - correct at every call site (pre-layout DOM construction, the target-page convergence loop, and any
later re-resolution) with no ordering dependency. See the method's own doc comment.

## Bug 2: a per-pass-transient value (`CssRectLeader.Width`) survives past its pass via `_wordsSizeMeasured`

`CssBox.MeasureWordsSize`'s `_wordsSizeMeasured` guard is a one-time-ever flag, correct for ordinary text
(measured once, never changes) but wrong for a `leader()` item: its width is recomputed every layout pass
by `CssLayoutEngine.ApplyLeaderFill`, not measured once. Two distinct manifestations, both only visible once
`HtmlContainerInt.PerformLayoutOnePass` grew a bounded multi-pass loop for `target-counter(_, page)`:

1. A box whose `Words` gets rebuilt (`CssBox.ParseToWords`/`ParseToWordsWithLeaders`) on a later pass
   creates brand-new `CssRect` instances - but `_wordsSizeMeasured` was already `true` from an earlier pass,
   so `MeasureWordsSize` returned immediately and the fresh instances kept their default zero `Width`/`Height`
   forever. Fixed by resetting `_wordsSizeMeasured = false` inside both `ParseToWords` and
   `ParseToWordsWithLeaders` - a rebuilt `Words` list always invalidates any earlier measurement.
2. A `leader()`-only box whose content never changes across passes (nothing ever calls `ParseToWords` on it
   again, since it has no `target-counter(_, page)` of its own) never gets fix (1)'s reset either - but its
   `CssRectLeader.Width` was still set to a real, non-zero value by the *previous* pass's `ApplyLeaderFill`.
   On the next pass, `MeasureWordsSize`'s guard skipped re-zeroing it, so `CssLayoutEngine.FlowBox` flowed
   the line's *initial* pass using that stale non-zero width instead of zero - pushing whatever came after
   the leader on the line past the line's right edge and forcing a wrap that shouldn't happen. Fixed by
   moving the leader-zeroing loop in `MeasureWordsSize` to run unconditionally, ahead of the
   `_wordsSizeMeasured` guard, since it's cheap (a single `OfType<CssRectLeader>()` pass over `Words`) and
   the reset must happen on *every* layout pass regardless of whether the box's content itself changed.

Both are logically pre-existing risks for `UseVariablePageWidth`'s own reflow loop and
`ReapplyPseudoElementContent`'s `string()` re-application too, in principle - they just never manifested
because neither of those existing consumers combines "content whose *own* resolved value depends on a later
layout pass" with "a value type whose measured size is itself pass-dependent" the way `leader()` +
`target-counter(_, page)` does. Worth remembering if a future multi-pass consumer hits similar staleness.

## Evidence

- `TargetCounterConvergenceIntegrationTests.TargetCounterWithLeader_ResolvesRealPageNumber_AcrossForcedPageBreak`
  (real forced page break, asserts the resolved page-number text and a non-zero leader width) caught bug 1
  outright (empty resolution) and bug 2's first manifestation (zero leader width) once bug 1 was fixed.
- Bug 2's second manifestation (spurious wrap, a leader-only box with no `target-counter` of its own) was
  only caught by rendering the TestHarness showcase (`target_counter_toc`) and visually inspecting the
  rasterized PDF (PDFium + MuPDF, per this repo's paint-verification convention) - the unit/integration
  tests' fixtures didn't happen to include a leader-only sibling box on a line that also contained a
  `target-counter(_, page)` elsewhere on the same line. Confirms CLAUDE.md's own showcase-catches-real-bugs
  convention held again here.
