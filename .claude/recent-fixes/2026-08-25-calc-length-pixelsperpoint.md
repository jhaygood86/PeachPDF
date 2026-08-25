# `calc()` length expressions now scale by `PixelsPerPoint` (#829)

Closes the gap carved out of issue #826 (see
[2026-08-25-em-rem-box-geometry-and-media-container-query-pixelsperpoint.md](2026-08-25-em-rem-box-geometry-and-media-container-query-pixelsperpoint.md)'s
"Deliberately not done" section) - a `calc()` length expression never received the `PixelsPerPoint`
catch-up multiply (issue #814's convention) that a literal absolute/`em`/`rem`/`ex`/`ch` length gets,
because `CssValueParser.ParseLength(string,...)`'s multiply is gated on the whole input string parsing as
a single `Length`, which a `calc(...)` string never does.

## Load-bearing idea

The catch-up multiply had to move from a single whole-result gate to a per-leaf decision inside
`CalcEvaluator.Evaluate`'s `DimensionCalcNode` case - mirroring how `emFactor`/`remFactor` are already
threaded through `CalcContext` per leaf. A single whole-result multiply would have been wrong for any
calc() mixing an absolute/em/rem/ex/ch leaf with a percentage or container/viewport-relative leaf (e.g.
`calc(50% + 10px)`), since the latter's basis is already reported in the box's `PixelsPerPoint`-inflated
space and must not be double-scaled. Added a `PixelsPerPoint` field to `CalcContext` (default `1.0`, a
no-op, matching every other call site of this idiom - font-size resolution, `@page` margins, which
deliberately want the raw true-point result), and applied `asLength.IsAbsolute ||
dimension.Unit is Em/Rem/Ex/Ch` - the exact same predicate `CssValueParser.ParseLength(Length,...)`'s own
catch-up gate already uses - to each `DimensionCalcNode` leaf's resolved pixel value before returning it.
`CssValueParser.ParseLength(string, double, CssBox)` (the box-aware overload the vast majority of
box-geometry properties resolve through) now threads its own `pixelsPerPoint` into the lower-level
`ParseLength(string, hundredPercent, emFactor, remFactor, ...)` overload's new optional parameter, which
in turn passes it into `CalcContext`.

## What running it (not just reading it) confirmed

- A fully-absolute calc() (e.g. `calc(100pt + 20pt)`) already resolved correctly even before this fix -
  Layer A's `CalcSerializer` folds a fully-absolute-only calc() expression to a literal length string at
  cascade time, so it never actually reaches `ParseLength`'s calc() branch at all; it takes the ordinary
  literal-`Length` path issue #814/#826 already cover. Confirmed by writing a discriminating regression
  test for this case first - it passed identically before and after the fix - and replacing it with a
  genuinely relative-unit case (`calc(2rem - 5px)`) that does discriminate.
- A percentage leaf mixed with an absolute/em/rem/ex/ch leaf in the same calc() (`calc(50% + 20px)`)
  needed its own regression test to prove the fix doesn't double-scale the percentage side - the
  containing block's own width, when set via an absolute length, is itself already `PixelsPerPoint`-scaled
  by issue #814, so this test's expected value has to account for that (a common source of an off-by-one
  `pixelsPerPoint` factor when hand-computing the expected value for this kind of fixture).

- A post-change review pass (this repo's convention) caught three things a first pass missed, all folded
  into this fix rather than left as separate findings: the `IsAbsolute || Em/Rem/Ex/Ch` catch-up predicate
  was being hand-written three times (`CssValueParser.ParseLength`'s two overloads plus
  `CalcEvaluator.Evaluate`) - extracted into one shared `Length.NeedsPixelsPerPointCatchUp` property so a
  future unit addition only needs updating in one place; the pre-existing `DimensionCalcNode { Unit: Pt }
  when context.ReturnPoints` fast path returned unscaled while the general case now scaled every other
  leaf type, an inconsistency that's a no-op today (no `ReturnPoints=true` caller threads a non-default
  `PixelsPerPoint`) but was a latent trap for a future one - fixed by gating the general case's own
  catch-up on `!context.ReturnPoints` too, so a `ReturnPoints=true` context never scales any leaf,
  matching the Pt fast path's existing intent; and the box-aware `ParseLength(string, double, CssBox)`
  ran a doomed-to-fail `Length.TryParse` on every `calc()` string just to learn it isn't a literal length -
  now short-circuited via the already-available `IsCalcFunction` check.

## Deliberately not done

- Did not thread `pixelsPerPoint` into `DomParser.ParseLengthToPdfPoints`'s calc() branch (`@page` margin
  geometry) - that call site deliberately wants the raw, unscaled true-point result (same as every other
  `@page` subsystem call site since issue #814), and the new parameter defaults to `1.0` there, a no-op.
- Did not thread it into `FontSizeResolver`'s calc() branch (font-size resolution) either, for the same
  reason - font-size resolves in a `returnPoints=true` space that a later step (`CreateFontInt`) divides
  by `PixelsPerPoint` itself, so scaling it again here would double-count.

## Evidence

- New regression suite `CalcLengthPixelsPerPointIntegrationTests.cs` (padding `calc(2em + 10px)` -
  issue #829's own repro, a `PixelsPerInch`-invariance check on the same fixture, `border-radius:
  calc(2rem - 5px)`, and `width: calc(50% + 20px)` against an already-inflated containing block) - all 4
  confirmed to fail against the pre-fix source with the exact unscaled values the bug predicts (e.g. `47.5`
  instead of `95` for the padding case at `PixelsPerInch: 144`), then confirmed to pass against the fix.
- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9223 passed, 9
  pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
