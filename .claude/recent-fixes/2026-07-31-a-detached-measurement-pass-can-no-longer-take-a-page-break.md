# A detached measurement pass can no longer take a real page break

The `invoice` showcase (US-Letter, full-bleed, `body { display:flex; flex-direction:column;
height: calc(11in - 1mm) }` with a `margin-top:auto`-anchored `<footer>`) rendered as 2 pages instead
of 1, with a nearly-blank second page holding only the footer's last paragraph, missing its own
background. The showcase's own content is nowhere near a page's worth too tall — a plain
`display:block` render of the same markup ends at 587pt of a 792pt page — so the pattern that
actually mattered was: `margin-top:auto` flush-anchoring a `flex-direction:column` item to a
container height that is close to (but under) the true page height.

## The load-bearing idea

`BlockConstraint.For`/`EndingAt` (`src/PeachPDF/Html/Core/Fragmentation/BlockConstraint.cs`) — read by
the §4.3 avoid/monolithic mover and the §5.4 widows/orphans mover in
`CssBox.PerformLayoutEpilogue` — decided whether a fragmentation question could be asked at all using
only `HtmlContainerInt.HasRealPageGrid`. That is true for the *entire* document, including inside a
flex/grid item's own measurement pass (`CssLayoutEngineFlex.MeasureItem`'s `PerformLayoutBlockified`),
which keeps a real page grid but explicitly detaches the ambient fragmentainer
(`HtmlContainerInt.DetachFragmentainer`, `CurrentFragmentainer = null`) so that content laid out at its
throwaway, provisional position cannot answer a fragmentation question — the whole reason
`DetachFragmentainer`'s own doc comment calls it "an absence rather than a suppressed flag." Neither
factory method consulted `CurrentFragmentainer`, so they silently reconstructed a *fresh*
`FragmentainerContext` from the real page grid regardless — the detachment did nothing for them.

Traced end to end: the `invoice` footer's nested row-direction `.cols` (three `<div>` columns) is laid
out via ordinary block flow, not through any engine's own item measurement — so measuring *its* three
column `<div>`s (`CssLayoutEngineFlex.MeasureItem`, itself properly detached) still ran with a real,
un-detached ambient ancestor state up the call stack. One column's own paragraph, at its throwaway
measurement position, happened to straddle the real page boundary. The widows mover's
`BlockConstraint.For` built a real constraint anyway, `TakeEarlyBreak`'s unconditional
`TranslateForEarlyBreak` fallback moved that paragraph to the next page's content top *during the
measurement*, and the column's measured cross-size came back roughly double its true height (the
gap between the paragraph's throwaway top and its post-break position). That inflated height fed the
footer's own pinned commit-pass height (`ItemContentCommit.CommitLayout`) at exactly the document's
real page bottom — landing the *next* sibling's real offset resolution (`CssBox.ResolveBlockChildOffset`,
which also calls `BlockConstraint.EndingAt`) flush on that same boundary, so it took a real early break
too.

The fix is two lines: both factories now also return `Measurement` (no fragmentainer) when
`container.CurrentFragmentainer is null`, mirroring the check `FragmentainerContext`'s own constructor
already makes for its ambient instances. See [`.claude/invariants/`](../invariants/) for nothing new
here — this closes the gap the existing detachment mechanism already declared it wanted.

## What was found by running it, not by reading it

Reading the widows/avoid movers in isolation, both looked correct — each already gates on
`!PositionAssignedByEngine` and `HtmlContainer.IsFragmenting` in the branches that matter for a *live*
pass. The defect was only visible by instrumenting an actual repro end to end (temporary
`Console.Error` tracing through `CssBox.LayoutBlockChildren`/`ResolveBlockChildOffset`,
`CssLayoutEngineFlex.MeasureItem`, and `FragmentPainter.PaintBackground`), which is what turned up
that `TakeEarlyBreak`'s *fallback* path (`TranslateForEarlyBreak`, taken whenever `CanBeLaidOutAgain`
correctly refuses because breaking is not live) has no matching `IsFragmenting` guard of its own — it
translates the box regardless, as an unconditional side effect of a boolean condition whose overall
truth value is then ignored. `BlockConstraint`'s missing guard is what let that fallback path be
reached from inside a detached pass at all.

## What was deliberately not done

A second, narrower, real defect turned up in the same trace — a flex item with no direct text of its
own (all content via children, e.g. the same `<footer>`) loses its own background/decoration once its
content genuinely (not spuriously) spans more than one page, specific to a box going through the flex
engine's commit pass. This is unrelated to the fix above (root-caused to
`FragmentEmitter.BuildDraft`'s decoration-materialization logic, not `BlockConstraint`) and left open —
filed as [#569](https://github.com/jhaygood86/PeachPDF/issues/569),
recorded in
[`.claude/accepted-gaps/flex-item-with-no-direct-text-loses-background-past-its-first-page-fragment.md`](../accepted-gaps/flex-item-with-no-direct-text-loses-background-past-its-first-page-fragment.md).

## Evidence

Full `net8.0` suite: 7361 passing, 9 skipped, 0 failed (unaffected suites: `Flex`/`Fragment`/`Widow`/
`Orphan`/`Grid`/`Table`/`Column`/`BreakInside`/`PageBreak` filter alone, 1648 tests, all green). Two
new `BlockConstraintTests` (`For_ReturnsMeasurement_WhenTheFragmentainerIsDetached`,
`EndingAt_ReturnsMeasurement_WhenTheFragmentainerIsDetached`) confirmed to fail without the fix and
pass with it. 100% diff coverage on the changed lines. `dotnet build PeachPDF.slnx -t:Rebuild`: zero
warnings. The full 79-showcase corpus regenerated cleanly through `PeachPDF.TestHarness`;
`invoice.pdf` is now 1 page, rasterized and visually confirmed complete (footer background, all
content, "Thank you" line all present and correctly positioned).
