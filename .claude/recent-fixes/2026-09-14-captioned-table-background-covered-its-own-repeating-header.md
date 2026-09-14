# A captioned table's background covered its own repeating thead/tfoot

A table with a `<caption>` and a background painted that background over its own `<thead>`/`<tfoot>`
(issue #1049). Both ingredients were required: a captioned table gets a dedicated
`CssBox.TableGridDecorationBox` for its border/background (issue #721), and a `<thead>`/`<tfoot>`
becomes a repeating-group `CapturedInstance` (`FragmentEmitter`) the moment a table has one at all —
whether or not the table actually spans multiple pages, a side effect of the `CapturedInstance`
generalization that fixed #917's superlinear pagination cost (see
[2026-09-07-fragmentemitter-linear-pagination-cost-for-917.md](2026-09-07-fragmentemitter-linear-pagination-cost-for-917.md)).

`FragmentEmitter.ChildrenOf` used to yield every `CapturedInstance.DetachedSourceRoot` (the header's
own detached subtree) ahead of everything else in the table's own child list, regardless of where
the decoration box actually sat there — even though `CssLayoutEngineTable.EnsureGridDecorationBoxStructure`
deliberately keeps it *first* in `_tableBox.Boxes` specifically so it paints behind real content. So
the header text painted first and the background painted second, silently covering it: text
extraction and content-stream assertions both looked correct, only a raster showed the defect (see
this repo's own painting-changes testing convention on exactly that class of false-positive).

## The fix, and the version of it that was wrong

`ChildrenOf` now interleaves each `DetachedSourceRoot` capture into `box.Boxes`' own order rather
than always emitting it first. The first attempt anchored each capture to the *current* position of
the `CssProxyBox` it replaced (found by scanning `box.Boxes`) — which happened to fix the reported
bug (the decoration box is always first, so it's always "before" wherever the proxy is) but broke
something else: `CreateHeaderProxy`/`CreateFooterProxy`'s `CssProxyBox` constructor always *appends*
the new proxy to the end of the table's own child list, so a repeating header's live proxy position
is always after ordinary rows regardless of where the header actually sits in the markup. Anchored
that way, a repeating `<thead>` painted after the table's own `<tbody>` rows — backwards relative to
the DOM, and a real defect (paint order matters the moment anything overlaps its own box: a row's
`box-shadow`, a transform, a tagged-PDF reading-order consumer) even though it happened to be
visually silent in the fixture that caught it, since header and body rows don't geometrically
overlap.

The corrected anchor is `CssProxyBox.SourceIndex` — the position `RemoveHeaderFooterFromTree`
recorded once, before any proxy existed, from its own `IndexOf` calls against `_tableBox.Boxes` (the
header's against the list with the decoration box already in place, since
`EnsureGridDecorationBoxStructure` runs first; the footer's against that same list with the header
already taken out). `RestoreStructureFromAnyPreviousRun` already relies on exactly this value,
inserted in ascending order, to put both groups back on the live tree; `ChildrenOf`'s new merge does
the same arithmetic to put them back into the *emitted* order instead, correctly accounting for the
footer's index being expressed relative to the header-already-removed list rather than the fully
original one.

## Evidence

Verified against unmodified `main` with a small console harness (printing each box as it's
visited during paint) that the corrected fix reproduces the *exact same* relative
header/footer/tbody/caption order `main` already produces — main's own order is `thead, tfoot,
tbody, caption` (the grid decoration box's background is simply spliced in ahead of all of them by
the bug), not `thead, tbody, tfoot, caption` as markup order alone would suggest; footer landing
before the body and caption landing last are both pre-existing, unrelated `_tableBox.Boxes`
ordering facts, not something this fix introduced or needed to change. Confirmed the wrong (v1)
anchor by reverting to it and rerunning the test suite: it reproduces exactly the failure a review
pass found, with the header painting after the body row. Two new regression tests
(`CssLayoutEngineTableTests.TableCaption_TableHasOwnBackgroundAndRepeatingHeader_BackgroundPaintsBeforeHeaderText`,
asserting both background-before-header *and* header-before-body; and
`RepeatingHeaderFooterCaptionBackgroundPaintOrderTests`, the same shape across every page of a
multi-page table with both a repeating header and footer) each fail on the pre-fix code and pass
after. Full `PeachPDF.Tests` suite on net8.0: green (one unrelated, pre-existing, environment-sensitive
memory-ratio benchmark test also fails identically on unmodified `main`). The
`PEACHPDF_VERIFY_FRAGMENT_PRUNING=1` pruning-parity oracle, scoped to every repeating-header/
multicolumn/proxy-box test file this change touches: green. Solution-wide `dotnet build
PeachPDF.slnx -t:Rebuild`: 0 warnings. Diff coverage against `origin/main`: 91% (the only uncovered
lines are a defensive "capture with no matching proxy" fallback that current invariants say should
be unreachable).
