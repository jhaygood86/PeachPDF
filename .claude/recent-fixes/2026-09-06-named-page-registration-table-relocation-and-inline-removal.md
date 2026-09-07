# Named-page registration cleanup: table relocation via OffsetTop, inline `page` removed (#149)

Two independent residuals in named-page (`page: <name>`) registration.

## Load-bearing idea

**Table relocation.** `CssLayoutEngineTable.LayoutBodyRows` has two whole-table relocation pre-checks
(headerless and header-present) that used to assign `_tableBox.Location` directly, then manually
`OffsetTop` the top captions (and, in the header-present case, the header box) to match. Direct
`Location` assignment bypasses `CssBox.OffsetTop`'s own `RegisteredNamedPageElement`/`MoveNamedPageElement`
re-sync, deferring it to `PerformLayoutImp`'s tail fallback - which runs *after* the row loop below has
already paginated against the pre-relocation geometry. Replaced both direct assignments with
`_tableBox.OffsetTop(pageBreakOffset)`, which re-syncs the registration immediately. Top captions are
`_tableBox`'s own DOM children (`AssignBoxKinds`'s `foreach (var box in _tableBox.Boxes)` walk populates
`_captionBoxes` from exactly that collection), so `OffsetTop`'s own recursion into `Boxes` already moves
them - the separate per-caption loop was doing (and, if kept alongside the new call, would have
double-applied) work `OffsetTop` now does on its own. The header box is genuinely detached
(`_headerBox.ParentBox = null` before this runs) precisely so it can repeat across pages, so it stays
outside that recursion and keeps its own explicit `OffsetTop` call.

**Inline `page` removed.** Per CSS Paged Media 3 §7.2, `page` applies only to boxes that create class-A
break points - block-level boxes, by definition. `CssLayoutEngine.FlowBox`'s two inline registration
sites (plain-inline exit, inline-flex) registered a `NamedPageElement` for an inline box anyway, at its
own raw (unsnapped, mid-line) Y - spec-marginal to begin with, and (per
`.claude/recent-fixes/2026-08-02-inline-string-set-and-named-page-corruption-across-reflow.md`) a real,
independent source of registration corruption across re-layout/resumed continuations. Removed both
registration blocks entirely rather than snapping them to `CssBox.NamedPageRegistrationY()` (the
alternative, more conservative fix) - a snapped-but-present inline registration would still be
spec-incorrect, just less visibly so. The sibling `NamedStrings` (string-set) finalization at both sites
is untouched; it's a different feature with no equivalent spec restriction.

## What running it (not just reading it) confirmed

- Removing inline `page` registration broke five existing tests that had used an inline `page` element
  purely as the *vehicle* for exercising two shared FlowBox mechanisms (unregister-before-register across
  re-layout; the `opensHere` gate across a resumed continuation) that also apply to `NamedStrings`. Three
  of the five (`MulticolLayoutIntegrationTests.NamedPageElement_InlineTarget_DoesNotAccumulateStaleEntriesAcrossReflow`,
  its InlineFlex sibling, and `ResumedInlineNamedStringLayoutIntegrationTests.NamedPageElement_InlineTarget_KeepsTruePositionAcrossResumedContinuation`)
  were deleted outright rather than adapted - each had an exact `NamedString`-based sibling test already
  exercising the identical shared mechanism, so keeping a now-permanently-empty-collection assertion
  alongside it would add nothing. The other two (`NamedPageLayoutIntegrationTests`' plain-inline and
  inline-flex "Registers" tests) were rewritten to assert the new no-op instead of deleted, since they
  have no `NamedString`-based sibling proving the same ground.
- `TableRelocatedByPreCheck_TailResyncMovesRegistrationToNewPage` (an existing test written to describe
  the *old* tail-fallback re-sync) still passes unchanged against the new `OffsetTop`-based mechanism -
  the final registered position is identical either way, only the timing differs (immediate vs.
  epilogue-tail) - confirming the tail fallback's own conditional re-sync (`PerformLayoutImp`'s
  `Math.Abs(RegisteredNamedPageElement.Y - registrationY) > PageBoundaryEpsilon` guard) correctly becomes
  a no-op rather than double-moving the registration.

## Deliberately not done

- Did not attempt the harder, "not observed in any real fixture" half of the original issue (a named-page
  table relocation whose *rows* fragment against pre-relocation bands) - the fix here closes the
  registration-timing gap that would enable it, but no repro fixture was constructed to prove the
  downstream row-fragmentation symptom itself no longer occurs.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9895 passed,
  9 pre-existing platform-gated skips - net decrease of 3 from the deleted redundant tests).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
