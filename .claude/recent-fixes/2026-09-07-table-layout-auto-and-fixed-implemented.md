# CSS `table-layout: auto | fixed` implemented (#918)

Closes #918.

## What was wrong

`table-layout: fixed` had no effect at all. The CSS-OM parsing side was already complete
(`TableLayoutProperty`, `Converters.TableLayoutConverter`, `PropertyFactory` registration, the raw
CSSOM `StyleDeclaration.TableLayout` getter/setter) — it validated and round-tripped correctly, but
nothing downstream ever read the resolved value. Two gaps, not one:

1. No `css-properties.json` entry, so the source generator never emitted a `CssBox.TableLayout`
   accessor for the layout engine to read in the first place.
2. `CssLayoutEngineTable.cs`'s entire column-width pipeline
   (`CalculateColumnWidths`/`DetermineMissingColumnWidths`/`EnforceMaximumSize`/`EnforceMinimumSize`)
   implements only CSS 2.1 §17.5.2.2's automatic (content-based) algorithm, unconditionally — no
   branch anywhere considered `table-layout`.

## The fix

1. Added a `table-layout` entry to `css-properties.json` (`TableArea`, plain keyword/string —
   mirroring `border-collapse`'s exact shape rather than an enum-backed `CssProperty<T>`, since every
   table keyword in this engine is consumed via `Keywords.*` string comparison already).
2. Added `_isFixedLayout` (a cached field, computed once in the constructor like `_isVertical`) and a
   new `CalculateFixedColumnWidths()` implementing CSS 2.1 §17.5.2.1's three-step algorithm: `<col>`
   width wins, then the **first row's own cell** width (never any later row), then an even split of
   whatever's left — with the table's own `max-width` still clamped in (a cheap, declarative check)
   but no cell content ever measured. `Layout()` now forks on `_isFixedLayout` right where the old
   four-method sequence ran; `CollapseColumnWidths()` stays outside the fork, unchanged, for both
   algorithms (`visibility: collapse` is CSS 2.1 §17.6.1, orthogonal to §17.5.2's algorithm choice).
3. **`_isFixedLayout` requires a specified (non-`auto`) table width, not just `table-layout: fixed`**
   — verified against MDN, not assumed: *"If the value of the `width` property is set to `auto` or is
   not specified, the browser uses the automatic table layout algorithm, in which case the `fixed`
   value has no effect."* An earlier draft of the implementation plan assumed browsers instead fill
   the containing block and split evenly under `width: auto` (reading CSS 2.1 §17.5.2.1's own
   permissive "a UA MAY... resolve via §10.3.3" text too literally); this was caught and corrected
   *before* writing any code, by fetching MDN's actual page rather than trusting the spec's optional
   wording. Getting this right up front avoided building an entire unneeded "fill and divide" code
   path for the `width: auto` case — the gate alone makes the automatic algorithm run untouched
   whenever the table's own width isn't specified.
4. Two small pure extractions kept the automatic path provably unchanged: `TryGetColumnElementWidth`
   (the `<col>`-reading loop body, shared by both algorithms) and `CellSpaceFor` (the
   width-minus-spacing-minus-borders arithmetic, shared by `GetAvailableCellWidth` and the new
   `max-width` clamp).

## What was found by running it, not by reading it

- The design's first pass (via a dedicated planning agent) proposed resolving `width: auto` by
  reusing `GetAvailableTableWidth()`'s existing "fall back to containing block width" return value and
  dividing it evenly — plausible from the CSS 2.1 spec text alone, and *wrong*. Fetching MDN's
  `table-layout` page directly (rather than trusting the spec's own "MAY" wording, or assuming prior
  knowledge of browser behavior) surfaced the real, simpler rule: no effect at all under `width: auto`.
  This was checked before any implementation code was written, not discovered by a failing test.
- All three new tests targeting the `<col>`-with-unsupported-unit fallthrough, the `max-width` clamp,
  and the proportional-surplus-distribution branch passed on the first run once written — the
  algorithm's line-by-line spec mapping left no ambiguity to debug, unlike a typical port-and-fix cycle.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0`: 9935 passed, 0 failed, 9
  skipped (pre-existing platform-gated skips) — up from 9933 before this change (10 new fixed-layout
  tests, 8 net after two pre-existing tests' fixtures were reused rather than duplicated).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings, 0 errors.
- Diff coverage (`diff-cover` against `HEAD`): 100% on the one changed production file
  (`CssLayoutEngineTable.cs`, 82 changed lines).
- The pre-existing `PerPageHorizontalReflowLayoutIntegrationTests.Table_StartingOnALaterDifferentlyMarginedPage_UsesThatPagesOwnMeasure`
  test already used `table-layout: fixed; width: 100%` and continued to assert the same 256pt-per-column
  result after this change — it now genuinely exercises the new fixed-layout path instead of the old
  automatic path it happened to pass through before.
- New `table_layout_fixed` showcase (`src/PeachPDF.TestHarness/Program.cs`) rendered and visually
  confirmed: the automatic table's content column swells as expected, the fixed table's three columns
  are visibly equal with the long cell's text wrapped, and the third table's `<col>`/first-row/colspan
  priority rules are all visible (including the second row's `width: 90%` being visibly ignored).
