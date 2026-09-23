# Font-relative units: `ex`/`ch`/`cap`/`ic`/`lh` measured from the font, plus root forms, in HTML, SVG and MathML

**Load-bearing idea.** A measured unit is a *ratio of the font's em* (`IFontMetricSource.GetRatio`), so `Length.ToPixels`
just multiplies it by the `emFactor`/`remFactor` every caller already carries. That is why none of the existing basis
overrides (margin-box em, gradient `emSizePt`, font-size parent size, `@page` context) or the `PixelsPerPoint` catch-up had
to change. The source is lazy — `DerivedStyle` implements it and touches a font only when such a unit is actually used, so
the hot path of every ordinary length is unchanged and nothing is allocated.

**Traps found by running it.**
- `lh` is defined as `ActualLineHeight / (font.Size * ppp^2)`: `ActualFont.Size` is true/ppp, `ToPixels` works in true points,
  and the caller's catch-up multiplies by ppp once more, so `1lh` lands exactly on `ActualLineHeight` only with that divisor.
  Tested at `PixelsPerPoint` 1 and 4/3.
- `line-height: 2lh` must read the *parent's* line-height (spec), and reading the box's own recurses. `ActualLineHeight` sets
  a guard only when the value is a calc or a `lh` length, so the unitless hot path pays nothing.
- A root-element unit on the root's own `font-size` must not consult the root's font (it is mid-resolution). The root walk
  starts at `ParentBox` exactly like `GetRemHeight`, so there is no element above and the spec fallback applies.
- `line-height` is inherited by adopting the *specified* value, so `line-height: 2lh` left raw compounds one factor per generation
  (a span under a `2lh` div got 120pt against 60pt). It is resolved to an absolute length at cascade time through the new
  `html.valueComputation: "line-height"` (`ResolveLineHeightValueComputation`), exactly as `font-size` is; a calc() containing `lh`
  still compounds, the same class as em-in-calc font-size. Found by review, not by the first round of tests, which only checked an empty leaf.
- `RFont` metrics are NOT all in one space: `Size` is requested/ppp, `NormalLineHeight`/`Height`/`Ascent` are in the requested-size space.
  HTML's `lh` divides by `Size*ppp^2` only because it is later multiplied by the true-point em and the catch-up; SVG/MathML (and
  `FontMetricMeasurement.Ratio`) divide by `Size*ppp`. Probed at ppp 4/3: Size 15, NormalLineHeight 25 for a 20-unit request.
- `font-size` with a font-relative unit refers to the *parent*: eager resolution (`ResolveFontSizeValueComputation`) passes
  `parent.DerivedStyle`, and the lazy path (calc, `r*` forms) wraps parent+owner in `ParentScopedFontMetrics`, allocated only then.
- `FontDescriptor.XHeight` silently falls back to `0.66 * ascender` when OS/2 has no `sxHeight`; that guess is worse than the
  spec's `0.5em`, so `HasAuthenticXHeight` gates `RFont.XHeightEm`. `CapHeight` falls back to the ascender, which *is* the spec's
  cap fallback.
- Tests that hard-coded `ex`/`ch` = 0.5em against the *default* font passed here only because of which default font this
  machine resolves — they would have failed on another CI OS. They now compute the expectation from the measured ratio, and the new
  suites pin Source Code Pro (zero advance exactly 0.6em) for literal numbers.

**SVG / MathML.** SVG's own suffix parser and MathML's `MathMetrics.Length` both go through the same
`FontMetricMeasurement.Ratio`. SVG threads the *current element's* font as `SvgTreeBuilder._lengthBasis` (set in `BuildElement`),
which also carries it into the generated `SvgPropertyContext` for `stroke-width`/`stroke-dasharray`/`stroke-dashoffset`. An SVG with
no `font-size` anywhere keeps `em` = 16 (`FontContext.SizeDeclared`), so unstyled output is byte-identical.

**Not done, on purpose.** `ic` uses the primary font in SVG/MathML (HTML uses per-codepoint fallback); `ic`/`ch` in vertical
writing modes use the horizontal advance; media queries and `@page` lengths keep the fallbacks (no font exists there).

**Evidence.** Full net8.0 suite and the source-generator suite green; new `FontRelativeUnitsIntegrationTests`,
`SvgFontRelativeUnitsTests`, Length/calc/MathML parser+metrics tests; showcase `font_relative_units` rasterized with PDFium and MuPDF.
