# SVG fill/stroke/fill-opacity/stroke-opacity fall back to the inherited value on invalid input

Closes [#675](https://github.com/jhaygood86/PeachPDF/issues/675), a follow-up to
[#599](https://github.com/jhaygood86/PeachPDF/issues/599)/[#676](https://github.com/jhaygood86/PeachPDF/pull/676)
found while fixing it — see
[.claude/recent-fixes/2026-08-07-svg-fill-rule-linecap-linejoin-fall-back-to-inherited-on-invalid-value.md](2026-08-07-svg-fill-rule-linecap-linejoin-fall-back-to-inherited-on-invalid-value.md)
for the shape this fix reuses.

## The load-bearing idea

`SvgTreeBuilder.ApplyCommon`'s four remaining call sites (`fill`, `stroke`, `fill-opacity`,
`stroke-opacity`) had the exact class of bug #599 fixed for `fill-rule`/`stroke-linecap`/
`stroke-linejoin`: they called `SvgPropertyRegistry.TrySet` and ignored its return value, so an invalid
attribute silently left the field at its `SvgElement` constructor default (black fill, no stroke,
opacity 1.0) instead of the ancestor's actual inherited value — worse for `fill`/`stroke` than the #599
case, since `css-properties.json` already declared `svg.invalidBehavior: "inherit"` for both and nothing
at build time catches an `ApplyCommon` block silently not implementing what the JSON already claimed
(`ApplyCommon`, not the generator, owns that decision — see CLAUDE.md's generator section). The fix is
the same `|| !SvgPropertyRegistry.TrySet(...)` shape the other seven properties in `ApplyCommon` already
use, plus flipping `fill-opacity`/`stroke-opacity`'s `invalidBehavior` from `"leave-unset"` to
`"inherit"` in `css-properties.json` (`fill`/`stroke`'s JSON already said `"inherit"`; only the C# call
site was wrong for those two).

## What didn't need to change

`SvgPropertyRegistry.TrySet`, `SvgValueParsers.TryParsePaint`/`TryParseOpacity`, and
`SvgPropertyRegistryEquivalenceTests.cs` — all already correctly return `false`/leave the field unset on
invalid input. Only the caller's post-`TrySet` decision changed, identical in shape to #676.

## Evidence

- Four new `SvgTreeBuilderTests.cs` cases (`Fill_InvalidValueOnChild_FallsBackToInheritedFromGroup`,
  `Stroke_InvalidValueOnChild_FallsBackToInheritedFromGroup`,
  `FillOpacityAndStrokeOpacity_InvalidValueOnChild_FallBackToInheritedFromGroup`) assert an invalid child
  value resolves to the ancestor's non-default value, not the hardcoded `SvgElement` default — the
  scenario the issue's `<g fill="red"><path fill="not-a-color" .../></g>` repro was about.
- Full `net8.0` suite: 8197 passed, 0 failed, 9 skipped (one `ContainerQueryLayoutIntegrationTests` case
  failed on an earlier run of the same unmodified suite and passed on a clean re-run — pre-existing
  cross-test-class flakiness, unrelated to this change).
- `dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings.
- `diff-cover` against `main`: 100% diff coverage (9/9 lines).
- New TestHarness swatches added to the `svg` showcase (`Program.cs`, "9 — Fill Rule & Opacity" section)
  rasterized with both PDFium and MuPDF at 200dpi — visually identical: the invalid-`fill`/`stroke`
  swatch still renders the group's purple fill / dark stroke, and the invalid-`fill-opacity`/
  `stroke-opacity` swatch still renders the group's 0.4 opacity (not reset to 1.0), in both renderers.
