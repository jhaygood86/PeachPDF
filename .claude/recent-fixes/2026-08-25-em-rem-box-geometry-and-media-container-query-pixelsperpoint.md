# em/rem box geometry, media/container query absolute lengths, and margin-box gradient em basis under non-default PixelsPerInch (#820/#826/#827)

Fixes three issues carved out as tracked follow-ups while fixing #823/#824 (see
[2026-08-25-gradient-em-rem-length-resolution.md](2026-08-25-gradient-em-rem-length-resolution.md) and
[2026-08-24-pixelsperinch-absolute-length-scaling.md](2026-08-24-pixelsperinch-absolute-length-scaling.md)
for that history): issue #826 (`CssValueParser.ParseLength`'s `em`/`rem`/`ex`/`ch` handling under a non-default
`PixelsPerInch`), issue #820 (`@media`/`@container` absolute-length feature comparisons ignoring
`PixelsPerPoint`), and issue #827 (`@page` margin-box gradient `content` resolving `em`/`rem` against
the document root instead of the margin box's own font-size).

## #826 - `ParseLength`'s em/rem/ex/ch under-scaling

`CssValueParser.ParseLength(Length,double,CssBox)` applied the `PixelsPerPoint` catch-up multiply
(issue #814's convention) only on its `IsAbsolute` branch. The load-bearing discovery: **the actual
bug was in the sibling `ParseLength(string,double,CssBox)` overload, not (only) the one the issue's own
"Where" section named.** `css-properties.json` stores `padding-*`/`width`/`height`/`margin-*`/
`border-*-radius`/etc. as raw C# `string` (`csharpDataType: "string"`), so `DerivedStyle`'s readers for
all of them call the *string* overload, which has an independent (and equally broken) `em`/`rem`
resolution path - fixing only the typed-`Length` overload, as the issue's own repro location suggested,
would have left the issue's own `padding:2em` repro unfixed. Both overloads got the same correction: undo
`GetEmHeight()`/`GetRemHeight()`'s device-scaling (`* pixelsPerPoint`, mirroring `CssImagePainter.ResolveGradientLength`'s
existing #823/#824 workaround) before handing them to `Length.ToPixels` as the em/rem basis, then extend
the existing absolute-length catch-up gate to also cover `Em`/`Rem`/`Ex`/`Ch`.

## #820 - media/container query absolute-length comparisons

`MediaQueryMatcher.CompareLength` resolved a feature's length via a box-less `Length.ToPixels` call and
compared the result directly against `MediaQueryContext.ViewportWidthPt`/`ContainerQueryContext.WidthPt`
- both despite their `Pt` naming actually the internal `PixelsPerPoint`-inflated layout space. Threaded a
`PixelsPerPoint` field through both context records (`MediaQueryContext.FromContainer`,
`HtmlContainerInt.BuildContainerQuerySizes`) down to `CompareLength`. **Went one step further than the
issue's own fix sketch** (which only mentioned the absolute-unit case): `CompareLength`'s `em`/`rem`
basis (`initialFontPt`, Media Queries 4's fixed 16px-initial-font-size) is a true-point constant, not a
device-scaled `CssBox.GetEmHeight()` read, so an em/rem feature value resolves to true points exactly
like an absolute one does in this call - it needs the identical catch-up multiply, not the em/rem-specific
double-correction #826 needed. The gate mirrors `CssValueParser.ParseLength(Length,...)`'s explicit
`IsAbsolute || Em/Rem/Ex/Ch` allowlist rather than the broader `Type != Percent` first drafted during
review - the broader gate was dormant today (this call never threads a real viewport/container basis into
`Length.ToPixels`, so those units always resolve to `0` regardless) but would have silently double-scaled
a viewport/container-relative feature value the moment a future change wired a real basis in, since that
basis would already be in the inflated space per `ParseLength`'s own viewport/container branches.

## #827 - margin-box gradient content's em basis

`MarginBoxRenderer.PaintImage` has no real, laid-out `CssBox` for a margin box, so it passes the document
root as `CssImagePainter.Paint`'s `box` parameter - a stand-in that #823/#824 made a live (but wrong) em
basis for a gradient's `em`/`ex`/`ch` stop-position/explicit-radius resolution. Added an optional
`gradientEmSizePt` parameter to `CssImagePainter.Paint`, threaded down to `ResolveGradientLength`, which
overrides `box.GetEmHeight()` with an already-resolved true-point font size when supplied (`null` for
every other caller keeps the existing box-derived behavior unchanged). `MarginBoxRenderer.PaintImage`
supplies `MarginBoxRenderer.ResolveFontSizePt(marginRule.Style, pageStyle)` - the same resolution its own
text `content` and width/height em-basis already use, gated to only run when the resolved image is
actually a gradient (a plain `url()`/SVG image never consults `gradientEmSizePt`). `rem` needed no
change: it always resolves against the document root regardless of context, which the root-box stand-in
already correctly is.

## Deliberately not done

- Did not refactor `CssImagePainter.ResolveGradientLength`'s own independent `em`/`rem` device-scaling
  workaround to call the now-fixed `CssValueParser.ParseLength` instead - redundant post-#826, but an
  unrelated simplification outside this fix's scope.
- **Did not fix `calc()` length expressions.** `ParseLength(string,...)`'s `PixelsPerPoint` catch-up
  multiply is gated on the whole input parsing as a single literal `Length`, which a `calc(...)` string
  never does - so no leaf inside a calc expression (absolute or, now, em/rem/ex/ch) ever receives the
  multiply. Not introduced by this change - the absolute-unit half of this gap predates it, back to issue
  #814 - but #826's correction to the em/rem basis changes the gap's shape (confirmed empirically:
  `padding: calc(2em + 10px)` at `font-size:20pt` resolves to the correct true-point `47.5` at both
  `PixelsPerInch: 72` and `144`, instead of `95` at the latter - previously it resolved to a *different*
  wrong value, `27.5`, at 144). Fixing this needs `CalcContext`/`CalcEvaluator` to carry
  `PixelsPerPoint` itself, a materially larger change than this PR's literal-length scope. Filed as
  [#829](https://github.com/jhaygood86/PeachPDF/issues/829); see
  `.claude/accepted-gaps/calc-length-not-scaled-by-pixelsperpoint.md`.

## Evidence

- `dotnet test PeachPDF.Tests/PeachPDF.Tests.csproj --framework net8.0` - full suite green (9219 tests,
  9 pre-existing platform-gated skips).
- `dotnet build PeachPDF.slnx -t:Rebuild` - 0 warnings.
- New tests in `EmRemBoxGeometryPixelsPerPointIntegrationTests.cs`,
  `MediaContainerQueryPixelsPerPointIntegrationTests.cs`, and one new fact in
  `MarginBoxRendererImageTests.cs`, all verified to fail against the pre-fix source (temporarily stashed
  the fix commits, reran, confirmed all 8 discriminating fixtures failed with the exact wrong values the
  bugs predict, then restored) before being confirmed to pass against the fix.
