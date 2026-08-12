# `caption-side` support (issue #705)

`caption-side: top | bottom` (CSS 2.1 §17.4) was fully unimplemented: `CssLayoutEngineTable
.AssignBoxKinds` had a bare `case Keywords.TableCaption: break;`, so a `<caption>` box was never added
to any of the engine's row/column bookkeeping and never assigned a position — it kept whatever
degenerate `Location`/`ActualBottom` it started with. The CSS-OM parsing/converter plumbing for the
property already existed (`CaptionSideProperty`, `Converters.CaptionSideConverter`,
`PropertyFactory`), but nothing downstream read it.

**Load-bearing finding, discovered by running it rather than by reading the diff**: giving `<caption>`
a real position needed two changes, not one. Registering `caption-side` in `css-properties.json` and
teaching `CssLayoutEngineTable` to position the caption (new `LayoutCaptionGroup`, called from
`LayoutCells` for a top caption and from `LayoutBodyRows`' Step 7 for a bottom one) produced a caption
with zero height in every test — `CssBox.PlacesItselfAsBlockBox` gates `LayoutContents`'s real dispatch,
and `table-caption` wasn't in its list, so the caption fell into the "copy the previous sibling's
geometry" fallback documented on that property. `table-cell` is in the same list for the identical
reason (its position is also engine-assigned, not frame-assigned), which is the precedent this fix
follows — adding `Keywords.TableCaption` alongside it.

**A second bug was caught by a review pass, not by the tests written alongside the feature**: the initial
`LayoutCaptionGroup` call sites used `_tableBox.ClientLeft` (already inset by the table's own left
border) as the caption's `x` together with `GetWidthSum()` (which independently adds both border widths
on top of the column/spacing sum) as its width — double-counting the left border and shifting the
caption right by exactly that amount. Invisible in the original three tests because none of them put a
border on the `<table>` element itself (only on `td`), and easy to miss visually at a thin border width
in a rendered screenshot. Fixed by using `_tableBox.Location.X` (the table's true border-box left edge)
instead. A fourth test (`TableCaption_TableHasItsOwnBorder_CaptionStillAlignsWithBorderBox`) pins this:
a table with its own `border` whose caption must still align with `Location.X`/`ActualRight`.

**Deliberately out of scope**: CSS 2.1 §17.4 models the table+caption as living inside an anonymous
"table wrapper box", with the table's own border/background applying to the grid only. PeachPDF has no
such wrapper — the caption is laid out inside the same `CssBox` that owns the table's border/background,
so a bordered/filled `<table>` visually encloses the caption too. Recorded as an accepted gap
(`.claude/accepted-gaps/table-caption-painted-inside-the-tables-own-border-background-box.md`, tracked
as [#721](https://github.com/jhaygood86/PeachPDF/issues/721)) rather than fixed here — closing it needs
a new anonymous-box kind, well beyond what stacking the caption above/below the grid required.

Whole-table page-break relocation (the two pre-checks in `LayoutBodyRows` that move `_tableBox.Location`
when the first row wouldn't otherwise fit) now also calls `caption.OffsetTop(pageBreakOffset)` for
already-laid-out top captions, mirroring the existing `_headerBox.OffsetTop(pageBreakOffset)` call —
verified visually (PDFium rasterization) with a table pushed to a second page: the caption and table
relocated together, nothing stranded on the first page.

**Evidence**: 4 new unit tests in `CssLayoutEngineTableTests.cs` plus a round-trip row in
`CssUtilsTests.cs`; full suite (8622 tests, net8.0 and net10.0) passes; 100% diff coverage
(`diff-cover` against `origin/main`); `dotnet build -t:Rebuild` on the whole solution is warning-free;
all 91 TestHarness showcases (including the new `table_caption` one) regenerate without error; visual
verification via both PDFium and MuPDF rasterization for top/bottom captions, a table with its own
border, and cross-page relocation.
