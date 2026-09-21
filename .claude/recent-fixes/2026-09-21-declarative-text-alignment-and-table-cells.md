# Declarative `Alignment(...)` aligned nothing, and `AlignRight()` did nothing on a table cell

Reported as "right-aligning text in a declarative table cell has no effect while HTML's `text-align: right`
does": `Text(t => { t.Alignment(TextAlignment.Right); ... })`, `AlignRight()`, `Alignment(End)` and friends all
rendered flush-left. Found while building the Factur-X invoice showcase, whose amounts could not be right-aligned.

## Root cause: a shorthand handed to a per-longhand registry

`TextSpanContainerBuilder.Alignment` called `CssPropertyFactory.Set(box, "text-align", value)`. That reaches
`CssUtils.SetPropertyValue` -> the generated `CssPropertyRegistry.TrySet`, which is **per longhand** (the
generator's whole model - one JSON entry per longhand). `text-align` is a shorthand (over `text-align-all` and
`text-align-last`, css-text-3 §6.1), so `TrySet` returned `false` and the value was silently discarded; the
cascade never hit this because it expands shorthands *before* it calls the registry. So `Alignment(...)` had been a
no-op **everywhere**, not just in cells. The pre-existing tests only asserted the builders "don't throw" for each
`TextAlignment` (`Alignment_...`/`TextAlignment_StartAndEnd_ResolveAgainstDirection`), which a no-op satisfies.

It looked cell-specific because the one place alignment appeared to work - a page footer - is centered by the
`@page` margin box's own `text-align: center`, not by the call.

Found by dumping the laid-out boxes (`TextAlignAll` was still `start` on the cell) rather than by reading the
layout engine, which was never the problem: the line-box alignment code (`CssLayoutEngine` keyed on
`lineBox.OwnerBox.ActualTextAlignAll`) is correct and already worked for HTML.

## The fix

- `CssPropertyFactory.SetTextAlign(box, keyword)` does the shorthand's expansion by hand: `text-align-all` takes the
  keyword and `text-align-last` resets to `auto`. `Alignment(...)` uses it. It goes through `Set`, so the names are
  recorded in `BuilderSetProperties` and a document-level stylesheet's plain rule still cannot override them.
- **A guard against the trap:** `TextAlignShorthand_IsRejectedByTheRegistry_SoBuildersMustSetTheLonghands` pins that
  the registry rejects `text-align` and accepts the longhands. Before fixing, a probe fed every property name the
  builders set (~60) through the registry: `text-align` was the only one rejected, so no other builder call is
  silently dropped today. Making `CssPropertyFactory.Set` throw on a rejected property would catch the whole class,
  but it would turn every currently-tolerated unparseable value into an exception, so it was not done.

## What `AlignLeft()`/`AlignCenter()`/`AlignRight()` mean on a table cell

Decided, not derived: they are implemented as `margin-left/right: auto`, and **margins do not apply to a table
cell** (CSS 2.1 §17.5), so on a cell they were a guaranteed no-op. "Align this container's own content" can only mean
aligning the cell's inline content there - which is what someone right-aligning a column of amounts means - so on a
`display: table-cell` box they now set `text-align` (`SetTextAlign`). Everywhere else they keep their box-positioning
meaning, unchanged (`AlignRight_OnARowItem_StillPositionsTheContainerWithAutoMargins` guards it). Header cells go
through the same builder, so header and body align together.

## A different bug with the same symptom - fixed separately

`column.Item().Width(100).AlignRight()` did not move the item, in or out of a table (the report's
`Column(col => col.Item().AlignRight()...)` variant). That needs a **cross-axis** auto margin, which
`CssLayoutEngineFlex` did not distribute (only main-axis ones, §8.1) - a `Row` item worked, a `Column` item did not.
It was left out of this change on purpose and fixed on its own:
[2026-09-21-flex-cross-axis-auto-margins.md](2026-09-21-flex-cross-axis-auto-margins.md).

## Evidence

- New tests in `DeclarativeApiIntegrationTests` assert word geometry after layout: for Left/Start/Center/Right/End the
  word sits against the right edge / centered / at the left, with and without cell padding, in a header cell, with a
  100%-wide child, and a neighbouring cell's alignment does not leak. 83 tests in the class pass.
- Rasterized the invoice showcase through **both PDFium and MuPDF**: amounts right-aligned, header labels aligned over
  their columns, identical in both.
- Full solution `-t:Rebuild`: 0 warnings, 0 errors.
