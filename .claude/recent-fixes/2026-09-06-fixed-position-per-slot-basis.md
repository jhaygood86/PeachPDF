# `position: fixed` and `background-attachment: fixed` now resolve against each page's own area (#146)

Four sites all resolved a fixed-positioned/fixed-attachment box against the document's single, base
page geometry instead of the current page's own (possibly `:first`/named/margin-overridden) area.

## Load-bearing idea

`CssBox.CommitBlockChildOffset`'s Fixed branch and `CssLayoutEngine.GetBoxHeight`'s `isFixedToPage`
branch resolve a fixed box's canonical `left`/`top`/`height` percentage basis exactly once, before its
own page is known - mirroring the existing ICB convention (`GetBoxHeight`'s `box == box.ContainingBlock`
branch, #145/#201): pinned to **page 1's own resolved band** (`PageGeometry.GetPage(0).BandWidth`/
`BandHeight`), not the document's base configured `PageSize`. `GetBoxWidth`'s own Fixed branch already
did this correctly (via `PageContentRightOf(resolvedBlockTop)`, which defaults to `box.Location.Y` - `0`
before the box is placed, i.e. page 0) - `GetBoxHeight` and `CommitBlockChildOffset` were the two
asymmetric holdouts.

`FragmentEmitter.ComputeFixedPageOffset`/`ComputeFixedSizeOverride` then correct that single canonical
value into a **per-slot delta** for every later page a fixed box repeats onto - but both gated
exclusively on `PageGeometryTable.HasSizeOverrides`, so a document with only a *margin* override (no
`size` override at all - the common `@page :first { margin: 0 }` case) never ran the correction, even
though the same content-area basis those methods reconstruct depends on margins just as much as on
sheet size. Widened both gates to also fire on `HasVerticalMarginOverrides`/`HasHorizontalMarginOverrides`.

`FragmentPainter.Decorations.cs`'s `PaintBackground` read `HtmlContainerInt.PageBoxRect` unconditionally
for a `background-attachment: fixed` layer's positioning area - the one call site that hadn't adopted
the `PageClipOverride ?? PageBoxRect` fallback `FragmentPainter.Paint`'s own `PushClip` and
`PdfGenerator.HandleLinks` already use for the per-slot window `PdfGenerator.AddPdfPages` sets before
painting each page.

## What running it (not just reading it) confirmed

- A full-`PdfGenerator`-pipeline test comparing raw PDF content-stream `cm` matrices across two
  differently-margined pages is **not** reliable for this bug: `PdfGenerator.AddPdfPages` emits the
  per-page margin-override translate as a *separate, earlier* `cm` operator (only present when that
  page's own margins differ from the base), so two pages' `cm ... /I0 Do` matrices, compared in
  isolation, are expressed in two different coordinate frames - a raw equality/inequality check on them
  proves nothing about this fix either way (confirmed both directions: an early draft's assertion passed
  identically with and without the production fix applied, for unrelated reasons - a size-only override
  changes `HtmlContainerInt.PageSize`/`MaxSize` per page through an already-correct, pre-existing
  mechanism unrelated to this bug, and a margin-only override's Y-axis difference is dominated by the
  paint-time translate itself, not by `viewportRect`).
- `HtmlContainerInt.PageClipOverride` is **also** the page's own content clip (pushed around all
  painting, not just backgrounds) - a test that sets it to a rect not containing the test box's own
  position clips that box out of the fragment entirely (zero draws recorded, not a wrong position),
  which is what an early draft of the isolated `RecordingGraphics` test hit before choosing a same-origin,
  smaller override instead of a shifted one.
- The reliable way to isolate this specific fix: extend the shared `RecordingGraphics` mock
  (`PeachPDF.Tests/TestSupport/RecordingGraphics.cs`) to record `DrawImage` destination rects (previously
  a no-op there), call `HtmlContainerInt.PerformPaint` directly with `PageClipOverride` set/unset, and
  assert the recorded rect - sidesteps PDF content-stream Y-flip and per-page-translate composition
  entirely, since `RGraphics` destination rects are in the same top-down layout space `PaintBackground`
  itself computes in.
- Confirmed each new/changed regression test fails against the pre-fix source with the exact wrong
  (base-page-basis) value the bug predicts, then passes against the fix, for all four sites independently
  (toggled via `git stash` on each touched production file in turn).

## Deliberately not done

- Did not attempt to make `background-attachment: fixed`'s positioning area reach into the page's own
  margins on every axis - `PageBoxRect`'s own pre-existing formula (unchanged here) already doesn't
  reach the bottom margin (only the right, via its `+ MarginRight` term), a pre-existing, untouched
  asymmetry this fix's per-slot correction (`PageClipOverride`, which mirrors that same shape) faithfully
  preserves rather than "corrects" - out of scope for a per-page-basis fix.

## Evidence

- New/updated regression tests, each independently confirmed to fail pre-fix: `FixedPositionPerPageOffsetLayoutIntegrationTests.FixedPercentOffset_ResolvesToEachPagesOwnArea_OnAMarginOnlyOverrideDocument`, `FixedPositionPerPageSizeLayoutIntegrationTests.FixedPercentSize_ResolvesToEachPagesOwnArea_OnAMarginOnlyOverrideDocument`, `BackgroundAttachmentFixedIntegrationTests.FixedAttachment_ViewportReflectsThePerSlotPageClipOverride_NotTheBasePageBoxRect`.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9898 passed, 9 pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
