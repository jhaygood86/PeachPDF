# SVG fill-rule/stroke-linecap/stroke-linejoin fall back to the inherited value on invalid input

Closes the gap tracked in `.claude/accepted-gaps/svg-fill-rule-linecap-linejoin-invalid-value-fallback.md`
(deleted by this fix) and [#599](https://github.com/jhaygood86/PeachPDF/issues/599).

## The load-bearing idea

`SvgTreeBuilder.ApplyCommon` already had the right shape for this — `stroke-width`/`stroke-miterlimit`/
`stroke-dashoffset`/`stroke-dasharray` check `SvgPropertyRegistry.TrySet`'s return value and assign
`inherited.X` when it's `false`. `fill-rule`/`stroke-linecap`/`stroke-linejoin` didn't: they called
`TrySet` and ignored the return value, so an invalid attribute silently left the field at whatever
`SvgElement`'s constructor default was (`Nonzero`/`Butt`/`Miter`) instead of the ancestor's actual
value — a real SVG 1.1 §11.4 deviation, not merely a coincidence of the three properties' defaults
matching their parsers' own fallbacks. The fix is exactly the three call sites changed to the same
`|| !SvgPropertyRegistry.TrySet(...)` shape the four other properties already use, plus flipping
`css-properties.json`'s `svg.invalidBehavior` for these three entries from `"leave-unset"` to
`"inherit"` (metadata only — parsed by `PropertyModelParser` but not read by the emitter, so it does not
by itself change generated code; `ApplyCommon` owns the actual fallback decision, as its own comment
block says).

## What didn't need to change

`SvgPropertyRegistry.TrySet` itself, `SvgValueParsers.TryParseFillRule`/`TryParseLineCap`/
`TryParseLineJoin`, and `SvgPropertyRegistryEquivalenceTests.cs`'s `[InlineData("bogus")]` cases — all of
those already correctly return `false`/leave the field unset on invalid input, which is exactly what the
fixed `ApplyCommon` call sites need to detect. Only the caller's post-`TrySet` decision changed.

## Evidence

- Two new `SvgTreeBuilderTests.cs` cases (`FillRule_InvalidValueOnChild_FallsBackToInheritedFromGroup`,
  `StrokeLineCapAndLineJoin_InvalidValueOnChild_FallsBackToInheritedFromGroup`) assert an invalid child
  value resolves to a non-default ancestor value, not the hardcoded default — the scenario this issue was
  about. No prior test (for these three properties, or for the four already-correct ones) exercised the
  invalid-falls-back-to-inherited path specifically.
- Full `net8.0` suite: 8194 passed, 0 failed, 9 skipped (pre-existing, unrelated).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- Cobertura coverage confirms both branches of all three changed conditionals are hit (600+ times each
  across the suite; not merely reachable).
- New TestHarness swatches added to the `svg` showcase (`Program.cs`, "9 — Fill Rule & Opacity" section)
  rasterized with both PDFium and MuPDF at 200dpi — visually identical: the invalid-`fill-rule` swatch
  still renders the evenodd donut inherited from its `<g>`, and the invalid-`stroke-linecap`/`-linejoin`
  swatch still renders rounded caps/joins inherited from its `<g>`, in both renderers.
