# A collapsed grid line resolves from its declared width, not from last pass's used width

`CollapsedBorderModel.Resolve` read each candidate's `ActualBorder*Width`. That cache is exactly what
`CssLayoutEngineTable.ApplyCollapsedUsedBorderWidths` overwrites, a few lines later in the same
`PerformLayout`, with the box-model **used** half-width — and `ClearCollapsedUsedBorderWidths` only
fires for a box that has *stopped* participating in a collapsed table, so the override survives to the
next pass. Resolution is that override's input, so on every pass after the first the resolver was fed
its own previous output: a 12pt declared border resolved to a 12pt line on pass one, 6pt on pass two,
3pt on pass three, with every cell shrinking to match.

`Add` now always reads `NaturalBorder*Width`, which is the same computation `ActualBorder*Width`
performs on a cache miss (`GetActualBorderWidth`, plus `none`/`hidden` → 0) but reads the declared
style each time. The `natural` parameter is gone rather than defaulted — there is no caller for whom
the cached read is correct, and the six `natural: true` call sites in `ResolveRepeatedGroupBoundary`
that had been opting in individually were the only ones that had ever been right.

## The trap this was hiding behind

**One layout pass is not a given, and nothing in the engine says so.** The comment on
`DerivedStyle.NaturalBorderTopWidth` asserted that `Resolve` "runs before that override is applied
(safe to read `ActualBorderTopWidth` directly)", which is true *within* a pass and false across them.
`ApplyCollapsedUsedBorderWidths`'s own sibling comment even lists the re-entry paths — "ShrinkToFit, a
§4.3 relocation, a per-page-width reflow" — three lines above the code that assumes they do not
happen. Both comments now say the same thing.

## What was found by running it rather than by reading it

- **The symptom is invisible in a small document, which is why it lasted.** Every minimal repro
  renders correctly: one table alone, a collapsed table plus its `separate` twin, a collapsed table
  after an 800pt spacer, even the new showcase section on its own with the full showcase stylesheet.
  It needs whatever re-enters the engine, and that is a property of the whole document, not of the
  table. Bisecting the `border_style` showcase found it **non-monotonic** — sections 3-6 plus the new
  section halve, sections 2-6 plus it do not, both two pages with the table at the same y on page 2.
  Do not try to characterise the trigger from the markup; reach for `LayoutRepeatedlyAsync` instead.
- **It was reachable from the committed showcase.** `docs/showcase/border_style.pdf` rendered every
  collapsed grid line in the new four-bevels section at 4.5pt for a declared `border: 12px` (9pt), the
  collapsed table reading 52.5×30pt against its `separate` twin's 75×57. That is what led here; the
  bevel work that added the section changed no geometry and did not cause it.
- **It predates the bevel work.** The identical halved geometry reproduces from a worktree at
  `55c66bac`, so this is not a regression from
  [2026-09-22-a-collapsed-grid-line-shows-both-bevel-faces.md](2026-09-22-a-collapsed-grid-line-shows-both-bevel-faces.md).

## Deliberately not done

- **`SetCollapsedUsedBorderWidths` still overwrites the `Actual*Width` cache**, rather than living in
  a field of its own that `ClientLeft`/content insets/`GetWidthSum` consult explicitly. Overwriting is
  what lets every existing reader see the used value with no call-site change, which is the whole
  design; the defect was a *resolver* reading a used value, not the override existing. Splitting the
  storage would be a much larger change for the same end.
- **The outer lines still overhang the table's border box**, unrelated and still
  [../accepted-gaps/collapsed-table-outer-grid-line-overhang.md](../accepted-gaps/collapsed-table-outer-grid-line-overhang.md)
  (#1257). Its measurements were taken on single-pass renders and are unaffected by this.

## Evidence

- `CollapsedBorderModelIntegrationTests.RelaidOutCollapsedTable_ResolvesTheSameLineWidthsOnEveryPass`
  lays the same 2×2 collapsed table out three times through `LayoutHarness.LayoutRepeatedlyAsync` and
  asserts every `HorizontalLineWidth`/`VerticalLineWidth`/emitted segment width stays at the declared
  12pt. Checked against the unfixed code: it fails with `Expected: 12 / Actual: 6`.
- Full suite green (13,120 passed, 9 skipped / net8.0); whole solution rebuilds with 0 warnings.
- `border_style` showcase re-rendered: the four collapsed swatches now paint 4.5pt bands on 9pt lines
  (was 2.25 on 4.5), verified through both PDFium and MuPDF, which agree.
