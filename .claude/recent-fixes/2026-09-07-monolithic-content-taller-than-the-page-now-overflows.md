# Monolithic content taller than the page now overflows instead of fragmenting

Closes #350.

## What was wrong

CSS Fragmentation Level 3 §2 says monolithic content (a replaced element, or a scroll container —
`overflow` other than `visible`/`clip`) may not be broken: where it would straddle a fragmentainer
boundary, it moves whole to the next one instead. `CssBox.PerformLayoutEpilogue`'s relocate-whole-box
mover already did this correctly for a box that fits in *some* single fragmentainer. But a scroll
container taller than *every* fragmentainer's own content band has nowhere to be relocated to
(`MonolithicContent.FitsInFragmentainer` never succeeds for it), so the mover's own `if` was skipped
entirely and execution fell through to ordinary layout — which, having no reason not to, took real page
breaks inside the box's own content exactly like any ordinary (non-monolithic) block would. This was a
known, deliberate, previously-accepted gap (documented in `docs/html-css-support.md`): the spec's own
"overflow the page" answer would have discarded every page past the first in a paginated renderer with no
viewport to scroll, which was judged worse than fragmenting the content anyway.

## The fix

`CssBox.LayoutContents`, when dispatching a monolithic (`MonolithicContent.IsMonolithic`) box's own
children to ordinary inline/block-child layout, now detaches the fragmentainer for the duration
(`HtmlContainerInt.DetachFragmentainer`/`RestoreFragmentainer`, plus `SuppressWordPageBreaks`) — the same
"nothing inside can ask a fragmentation question at all" mechanism flex/grid/table already use for their
own measurement passes, and which `DetachFragmentainer`'s own doc comment already named "a monolithic
subtree" as an intended use case, just never wired up. With no fragmentainer to break against, the
subtree's content lays out as one continuous, unbroken run — exactly like any other tall content already
does (a 620pt block, a tall `<img>`), each page's own paint clip showing its own slice of it, with no new
geometry/clip mechanism needed. Applied unconditionally (not gated on "won't fit anywhere"): it changes
nothing when the content *does* fit on one page (nothing would have taken a break there either way), and
the epilogue's own relocate-whole-box mover still moves an unbroken, too-tall-for-here-but-fits-on-the-
next-page box exactly as before.

Two exclusions matter:
- **A box that actually dispatches to the multi-column engine** (`EstablishesMultiColumnContext &&
  Boxes.Count > 0 && !ContainsInlinesOnly`, not `EstablishesMultiColumnContext` alone) — a column is a real
  fragmentainer in its own right per css-break-3 §2, and drives its own nested fragmentation regardless of
  this box's own monolithic status. The narrower condition matters: `ContainsInlinesOnly` is checked
  *before* the multi-column branch in `LayoutContents`'s own dispatch, so a monolithic box holding only
  inline content never reaches `CssLayoutEngineColumns` at all even with `columns: 2` set — excluding it by
  style alone (as an earlier version of this fix did) left that shape unsuppressed, caught by code review
  and confirmed with a fixture (`FragmentainerPasses` stayed `>1` instead of the expected `1`).
- **`table-cell`/`table-caption`** — `td`/`th` get `overflow: hidden` from the UA stylesheet
  (`CssDefaults.cs`), making every cell "monolithic" by `IsScrollContainer`'s own overflow test. A cell's
  own fragmentation across pages is `CssLayoutEngineTable`'s long-standing, well-tested feature, unrelated
  to the cell's own overflow value — both display types are always positioned by that engine
  (`PositionAssignedByEngine`) rather than by this generic dispatch's own frame.

A second, smaller fix was needed alongside: `CssBox.ForcedBreakTopFor` computed a forced break's
(`break-before`/`break-after`) target via pure page-grid arithmetic (`PageTopOf`/`SlotStartingAt`), with no
reference to whether a fragmentainer was even attached — so a `break-before: page` *inside* a suppressed
monolithic subtree was still being honored, manufacturing a page break the detach was supposed to
prevent (itself a form of "splitting" §2 forbids). Fixed by having it decline (return `null`) when
`HtmlContainerInt.SuppressWordPageBreaks` is set — the same flag flex/grid's own measurement pass already
sets, not `IsFragmenting`, since that is equally false once a layout pass has simply finished and none is
running at all (a shape `ForcedBreakTargetIsTheFramesTests` deliberately exercises, asking the target
post-layout to pin that it is re-derived rather than latched — using `IsFragmenting` there broke all nine
of its cases). This also means a forced break inside a flex/grid item's own *measurement* pass is no
longer spent before the real fill sees it, a smaller, incidental step toward #395 (not itself sufficient
to close it).

## What was found by running it, not by reading it

- The first version of this fix (gating only on `MonolithicContent.IsMonolithic`, with no table-cell
  exclusion) passed a quick manual check but broke 27 existing tests when run against the full suite — the
  UA stylesheet's `td, th { overflow: hidden }` was not something a purely-reasoned review caught.
- The first version of the `ForcedBreakTopFor` guard (checking `IsFragmenting`) broke all nine cases in
  `ForcedBreakTargetIsTheFramesTests`, which deliberately call the method *after* layout has finished, to
  prove the target is re-derived rather than latched — a scenario where `IsFragmenting` is unconditionally
  false regardless of monolithic suppression. Switched to `SuppressWordPageBreaks`, which is only ever true
  during an actual suppressed pass.
- Verified visually: generated a real PDF (A4, a scroll container spanning 3 pages with a
  `break-before: page` in the middle) and rasterized it with PyMuPDF. The forced break's target text
  follows the preceding line immediately, mid-page, with the scroll container's own background color
  painting continuously across the page boundary with no gap.

## What was deliberately not done

`#484`'s remaining case (a single *line* taller than a fragmentainer band, currently claimed by and drawn
in both fragmentainers rather than clipped to one) is a related but distinct mechanism — a line has no
further content to suppress breaking *within*, so this fix's "let the subtree flow unbroken" approach does
not apply to it. That case still needs the harder, previously-deferred "let the emitter clip rather than
continue" `FragmentEmitter` change; left as the existing, accurate accepted gap
(`.claude/accepted-gaps/a-straddling-line-is-drawn-in-both-fragmentainers.md`, tracking #484) rather than
guessed at here.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9902 passed, 0 failed, 9 skipped.
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- New tests in `MonolithicContentLayoutIntegrationTests.cs`: a scroll container taller than any page now
  ignores a forced break inside it and spans several pages instead (fails against the pre-fix code); a
  table cell taller than any page still fragments normally via the table engine, unaffected by the new
  exclusion.
- Manual PDF generation + PyMuPDF rasterization, visually confirmed.
