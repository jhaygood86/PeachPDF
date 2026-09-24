# A float after inline content shares one line with it, instead of splitting the run in two

Issue #1038, and the gap file
`.claude/accepted-gaps/a-float-after-inline-content-is-placed-on-the-next-line.md`, narrowed (not
deleted - see below) to
[the fix that closed it](2026-09-24-a-float-taller-than-a-page-shows-all-of-its-lines.md).

A floated child now joins the same inline formatting context as the inline content around it,
regardless of source order - `XY <span style="float:left">ZZZZ</span> more words` places `XY`, the
float, and `more words` all on one line, matching browsers. Before this, `DomParser.
CorrectInlineBoxesParent` wrapped the inline run before the float into its own anonymous block (CSS 2.1
§9.2.1.1), because §9.7 blockifies the float and the wrapping loop's own mixed-content test
(`ContainsVariantBoxes`) saw it as the "block" half of the mix - so the wrapper's own line ended at the
float, and the float then landed as an ordinary block-level sibling, on top of the content it should
sit beside.

## The load-bearing idea

Three predicates, all in `DomParser.cs`/`DomUtils.cs`, needed the same fact - a float joins the inline
run - but for different reasons, and getting only one of them right reintroduced the bug in a new
shape each time:

- `JoinsTheInlineRun` (the wrapping loop's own per-child test): a float now joins, so a run like
  `text, float, text` is never split at the float in the first place.
- `DomUtils.ContainsInlinesOnly` (the layout dispatch a box's *own* content is routed by): a float now
  counts as "inline-compatible", so a box holding only floats-and-inline-content dispatches through
  `CssLayoutEngine.CreateLineBoxes`/`FlowBox` (the inline path) rather than `CssBox.
  LayoutBlockChildren` (the block-children path) for its own children.
- `ContainsVariantBoxes` (whether wrapping is needed *at all*): a float must count toward **neither**
  half of the mixed-content test, not just toward the "inline" half. Counting it as `!IsInline` for the
  block half (the pre-#1038 reading) and `JoinsTheInlineRun` for the inline half (needed for the first
  bullet) made a **lone** floated child satisfy both by itself - reporting variance in a box with only
  one child - and `CorrectInlineBoxesParent` then wrapped that lone float into a new anonymous block
  whose own lone child was the same float again: unbounded nested single-float wrappers, a real stack
  overflow reachable from `<div><div style="float:left"></div></div>` alone. The fix reads
  `!child.IsInline && !(IsFloated || IsOutsideMarker)` for the block half and
  `child.IsInline && !(IsFloated || IsOutsideMarker)` for the inline half - float and outside-marker
  both excluded from *both* sides, so neither can single-handedly manufacture "variance".

`CssLayoutEngine.FlowBox` then needed an actual dispatch branch for a floated child reached mid-walk:
it positions the float from the live inline cursor (`coordinates.Line.FlowTop ?? coordinates.CurrentY`
+ the float's own `ActualMarginLeft`/`Top`, not through `CssBox.PlaceAndSizeBlockChild`'s block-sibling
margin-collapse math, which has no relationship to a shared line), calls the existing
`CssLayoutEngine.FloatBox` for the actual left/right placement scan and `clear` handling, then lays its
own content out via `CssBox.LayoutContentAtItsAssignedPosition` - the same entry point
`FlowAtomicBlockContentChild` already uses for an inline-block with block-level content, reusing that
already-tested machinery rather than duplicating it.

## What running it turned up (three real bugs beyond the design)

1. **The `ContainsVariantBoxes` infinite regress above** - found by running the very first fixture
   (`Float_PushesFollowingSiblingTextToTheRight`, unrelated to the new "shares a line" feature at all)
   under the full test suite, which crashed with a native stack overflow. Fixed as described above.
2. **A float that is now genuinely `ContainsInlinesOnly`'s ONLY reason a box routes through
   `CreateLineBoxes` never got its own "block inside inline" or anonymous-block-run correction**, because
   `CorrectBlockInsideInlineBlockFormattingContexts`/`CorrectInlineParentsInsideInlineBlockFormattingContexts`
   (the specialized per-child walks those two dispatch paths use once `ContainsInlinesOnly` is true) had
   no arm for a floated child at all - it was silently skipped, neither recursed into nor corrected. Both
   walks gained an `else if (child.IsFloated)` arm that recurses into the float exactly as the *generic*
   per-child recursion already would have, for a box the float is no longer routed through.
3. **A real, reachable infinite recursion**: a floated, word-holding leaf box with no children of its
   own (`<img align="left">`'s presentational-attribute float is the practical case, via `DomParser.
   CorrectReplacedElementBoxes`'s `IsReplacedBlockWrapper`) hits `FlowBox`'s own self-iteration
   convention (`boxes = [box]`, used when a box holds words directly but no child boxes). On that call,
   the loop's `b` *is* `box` - so the new float-dispatch branch re-entered `FlowFloatChild` on the same
   box that was already mid-placement, forever. Fixed by excluding self-iteration
   (`!ReferenceEquals(b, box)`) from the float branch, falling through to the ordinary word-flow path
   instead - correct, since by the time this inner call runs, the float has already been positioned by
   whichever caller reached it. **Found only by running the FULL suite** (not any targeted fixture): the
   crash pointed at three unrelated `PresentationalAttributeIntegrationTests` (`ImgAlign*`) tests once a
   `RuntimeHelpers.EnsureSufficientExecutionStack()` probe (added temporarily at the handful of suspect
   recursive entry points, removed once the cause was found) converted the native crash into a catchable
   exception with a real managed stack trace.
4. **`CssBox.GetMinMaxSumWords`' own float branch unconditionally hung the trailing space before a
   float** (`maxSum -= trailingSpace; trailingSpace = 0`), on the pre-#1038 assumption that a float could
   never be followed by more content on the same line. Once it genuinely can
   (`XY <span style="float:left">ZZZZ</span> more`), that assumption discarded the ONE interior space
   `DomParser.CollapseWhitespaceRun`'s own cross-boundary collapse had already consolidated onto the
   preceding word run - undercounting by one space regardless of what followed. Fixed by leaving
   `trailingSpace` exactly as the preceding word run set it: `GetMinMaxWidth`'s own epilogue still hangs
   it if the float is genuinely last, and if more content follows, adding its word width on top of the
   still-pending space reconstructs the single interior space either way.
5. **A float sharing a line with several words after it clamped every one of them back to its own right
   edge**, not just the first: the new `GetIntersectingInlineFloat` (feeding `FlowBox`'s local
   `LeftFloatAt`/`RightFloatAt`, which also consult the pre-existing `DomUtils.
   GetLastLeftIntersectingFloatBox`/`GetLastRightIntersectingFloatBox` for a float that precedes the
   box being flowed as an ordinary sibling) checked only the float's *vertical* span, not - for a LEFT
   float - whether the cursor had already advanced past its *horizontal* one. `DomUtils.
   GetLastLeftIntersectingFloatBox` already gets this right, via a point-collision test against
   `coordinates.CurrentX` (see its own remarks); the new helper needed the same convention, or every
   word past the first piled up at the float's edge instead of advancing. A right float is unaffected -
   it already uses a lookahead convention independent of the cursor's current position, matching
   `GetLastRightIntersectingFloatBox`'s own documented asymmetry.

## What the post-change review pass turned up

6. **Widening `DomUtils.ContainsInlinesOnly` to also report `true` for a box holding only floats could
   have rerouted a multi-column container whose direct children are all floats away from
   `CssLayoutEngineColumns` and into the inline-flow path instead** (`CssBox.LayoutContents`'s own
   inline-vs-block dispatch reads that same predicate). Fixed by widening the dispatch condition to
   `EstablishesMultiColumnContext && Boxes.Count > 0 && (!DomUtils.ContainsInlinesOnly(this) ||
   Boxes.Any(b => b.IsFloated))` - a multicol container with any floated child now always reaches the
   columns engine regardless of what `ContainsInlinesOnly` says, matching what it did before this
   change touched that predicate at all.
7. **Investigating the fix in (6) surfaced a separate, pre-existing bug it does not cause and is out of
   scope to fix here**: `CssLayoutEngineColumns.Layout`'s own item-collection filter
   (`!b.IsExcludedFromFlow`) excludes every floated child from the list of boxes it distributes into
   columns, and nothing lays a filtered-out float out afterward the way an absolutely/fixed-positioned
   descendant is (through a separate out-of-flow pass) - so a multicol container whose children are
   *all* floats gets no column layout at all, leaving every float at its default, all-zero geometry.
   Confirmed pre-existing (not introduced by (6)) by reproducing the identical all-zero result against
   unmodified `origin/main` with this fix's changes stashed away, for the exact same repro. Recorded as
   [a-multicol-containers-floated-children-are-never-laid-out.md](../accepted-gaps/a-multicol-containers-floated-children-are-never-laid-out.md)
   (issue #1203) rather than fixed here, since it is a defect in the columns engine's own item
   handling, not in anything this change's dispatch logic decides.
8. **Checked, not a bug: a float immediately followed by a forced break.** Issue #1038 made a float a
   reachable predecessor of a `<br>` within one inline formatting context, which the review pass
   flagged as untested - `XY <span style="float:left">ZZZZ</span><br>more` could not have arisen before
   this change (a float could previously only ever be the last thing measured on a line). Reasoned
   through and then verified empirically
   (`AFloatImmediatelyFollowedByAForcedBreak_StillHangsTheSpaceBeforeIt`, `IntrinsicWidthWalkTests.cs`):
   the float branch's own "leave `trailingSpace` pending" design (bug 4, above) already generalizes
   correctly here, because the `<br>` word branch is one of the two places (alongside `GetMinMaxWidth`'s
   own epilogue) that resolves a pending trailing space by hanging it - so the float being "genuinely
   last on this line" still hangs the space even though the break itself follows it. No production code
   change; added as new coverage for a combination the existing suite had never exercised.

9. **A float's own left-side box spacing leaked into the NEXT sibling's cursor.** `FlowBox`'s per-child
   prologue computes `leftSpacing`/`rightSpacing` from a child's real margin/border/padding for
   anything that isn't `position: absolute`/`fixed`, and (when the child opens the line) adds
   `leftSpacing` straight onto `coordinates.CurrentX` before the float-dispatch branch added by this
   change even runs. `FlowFloatChild`/`FloatBox` never read `CurrentX` for the float's own placement -
   it comes from `blockBox.ClientLeft`/`ActualMarginLeft` and `FloatBox`'s own placement scan - so that
   add is dead for the float's OWN iteration, but the float branch `continue`s without ever resetting
   `CurrentX` again, so the polluted value survives into the NEXT sibling's iteration unchanged. When
   the float is the first FLOAT on its line (nothing for `GetIntersectingInlineFloat`'s point-collision
   check to re-anchor against yet), the leaked value can make that check wrongly conclude the cursor
   already passed the float, so the sibling after it is not re-anchored to the float's true right edge
   (CSS 2.1 §9.5.1 rule 6) and instead trails further along the line than it should. Fixed by folding
   `b.IsFloated` into the same `excludedFromLineSpacing` guard absolute/fixed children already used, so
   a float's own margin/border/padding-left/right never reaches `CurrentX` at all.
   Found by an independent post-change review pass reading the new dispatch code line by line, not by a
   failing test - none of this fix's own tests declared box-model insets on the leading edge of an
   inline-shared float. `FloatWithLeftInsetAmidInlineContent_DoesNotLeakItsOwnInsetIntoTheNextSiblingsCursor`
   (`FloatLayoutRegressionTests.cs`) closes that gap: confirmed to fail without the fix (measured 9.79pt
   too far right for its own fixture numbers) and pass with it, by toggling the guard locally both ways
   - the exact numeric trigger depends on `FloatBox`'s own placement scan as well as the preceding run's
   advance, not on a simple closed-form threshold, so the fixture's specific values are empirically
   confirmed rather than derived.

10. **Rebasing onto `main` surfaced a real gap introduced by combining this change with an unrelated
    refactor that landed on `main` in the meantime.** Issue #1105's `FitAtomicInlineOnLine` (extracted
    from what were three separately-duplicated inline-block/inline-table/inline-grid/inline-flex line-fit
    checks) queries `DomUtils.GetLastRightIntersectingFloatBox`/`GetLastLeftIntersectingFloatBox`
    directly - the ancestor-only lookups `LeftFloatAt`/`RightFloatAt`'s own remarks describe as unable to
    discover a float #1038 placed directly among the SAME box's own inline content. Two PRE-EXISTING
    tests (`AtomicInlineFitCheck_NarrowedByARightFloat_StillWraps`,
    `AtomicInlineThatWraps_StartsAfterALeftFloatStillActiveOnTheNewLine`,
    `AtomicInlineLevelBlockContentIntegrationTests.cs`) turned out to already cover exactly this shape -
    their float sits as a direct child of the same `div` as the atomic inline-block being wrapped, which
    #1038's own `ContainsInlinesOnly` widening puts in one inline formatting context with no anonymous
    wrapper splitting them apart any more (there was one before #1038, which is why these tests originally
    passed under the old box-tree shape - the float was an ordinary preceding SIBLING of the wrapper,
    exactly what the raw `DomUtils` lookup can find). Confirmed both fail without the fix and pass with it
    by toggling `FitAtomicInlineOnLine`'s two float lookups between the raw `DomUtils` calls and
    `RightFloatAt`/`LeftFloatAt` locally. Fixed by promoting `LeftFloatAt`/`RightFloatAt` from local
    functions inside `FlowBox` to shared `private static` methods (taking `coordinates`/`reference`
    explicitly, since a local function's closure over `FlowBox`'s own `coordinates` isn't visible to a
    separate top-level method) and pointing `FitAtomicInlineOnLine` at them instead of the raw calls - no
    new test needed, since the two existing ones already exercise it precisely once routed correctly.

## Deliberately not done, and why it's safe

**Full pagination of a float's own content.** If a float's own content is taller than fits the
remaining page, the excess is simply never laid out - it does not continue on a later page. This
matches the *identical*, already-accepted limitation `FlowAtomicBlockContentChild` has for an
inline-block with block-level content (neither call site threads a nested `PendingBreakToken` any
further), so this is not a new class of limitation introduced by this fix - see
[the fix that closed it](2026-09-24-a-float-taller-than-a-page-shows-all-of-its-lines.md)
(filed as issue #1201). It is safe rather than corrupting: the float is positioned once, its content
layout is the ordinary fragmentainer-aware dispatch, and nothing re-enters or duplicates it. The
*surrounding* document's own pagination is unaffected either way, confirmed by a dedicated test
(`FloatAmidInlineContent_SurroundingContentResumesAcrossAPageBoundary_WithNoWordLostOrDuplicated`) that
forces a 200-word paragraph with a float present across a page boundary and checks every word survives
exactly once.

**Vertical writing mode.** `CssLayoutEngine.CreateVerticalLineBoxes` already had its own, independent
mechanism for a floated descendant (`MeasureAndCollectWordsInDocumentOrder` pulls it out of the word
stream via `IsOutOfFlow`, `LayoutOutOfFlowDescendants` places it through the ordinary
`CssBox.LayoutBlockChild` entry point) - unaffected by this change, and it already worked correctly
once the DOM-tree shape stopped hiding it behind an anonymous wrapper. One existing test
(`VerticalRl_Float_RoutesThroughLayoutVerticalBlockChildren_NotWordStream`) asserted the *old*
DOM-tree shape as a routing guard; updated to assert the new one (the box now genuinely reaches
`CreateVerticalLineBoxes` directly, and both text runs still end up in one set of line boxes,
unbroken by the float between them) since that was the specific browser-mismatched assumption #1038
set out to remove.

## Evidence

`FloatLayoutRegressionTests`, eight new fixtures (the accepted-gap's own repro, a same-shape regression
guard for two adjacent floats with nothing between them, text on both sides with real wrapping around
a narrow float, the float:right counterpart, the two pagination fixtures above, and the review-pass
CurrentX-leak fixture), all 29 in the file passing. `IntrinsicWidthWalkTests` gained one fixture for a
float immediately followed by a forced break. `MulticolLayoutIntegrationTests` gained one fixture
pinning that a multicol container whose children are all floats still dispatches to
`CssLayoutEngineColumns` rather than the inline-flow path (see
[a-multicol-containers-floated-children-are-never-laid-out.md](../accepted-gaps/a-multicol-containers-floated-children-are-never-laid-out.md)
for the separate, pre-existing gap that dispatch fix uncovered but does not itself fix). Full suite
green on net8.0 (12,357 passed / 0 failed / 9 skipped), 100% diff coverage on the changed production
lines, solution rebuild with 0 warnings across net8.0/net10.0/net11.0.
