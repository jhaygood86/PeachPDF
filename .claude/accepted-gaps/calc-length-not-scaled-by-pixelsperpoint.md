# `calc()` length expressions are not scaled by `PixelsPerPoint`

`CssValueParser.ParseLength(string, double, CssBox)` applies the `PixelsPerPoint` catch-up multiply
(issue #814's convention: an absolute or, since #826, `em`/`rem`/`ex`/`ch` length needs one extra
`* pixelsPerPoint` to land in the box's internal, DPI-inflated layout coordinate space) only on its own
final return statement, gated by `Length.TryParse` succeeding on the *whole* string as a single `Length`.
A `calc(...)` expression never satisfies that gate - it takes the earlier `TryGetCalcFunction` branch and
returns straight from `CalcEvaluator.Evaluate` - so no leaf inside a `calc()` expression (absolute,
`em`/`rem`/`ex`/`ch`, or any combination) ever receives the catch-up multiply, regardless of
`PixelsPerInch`.

Not new: already true for absolute-unit calc leaves before issue #814's fix, deliberately left out of
scope then. Issue #826's fix (correcting `em`/`rem`/`ex`/`ch` box geometry under a non-default
`PixelsPerInch`) changed the *shape* of the em/rem portion of this gap without closing it -
`ParseLength(string,...)` now passes a corrected, true-CSS-point em/rem basis into `CalcContext`, so a
`calc()` em/rem leaf resolves to the correct true-point value, but that value is still never inflated by
the final `pixelsPerPoint` multiply a literal (non-calc) em/rem length now gets - so it's still wrong
under non-default `PixelsPerInch` (now off by one factor of `pixelsPerPoint`, versus roughly
`pixelsPerPoint²` before #826). Confirmed empirically: `padding: calc(2em + 10px)` at `font-size:20pt`
resolves `ActualPaddingTop` to `47.5` (the correct true-point value, unscaled) at both `PixelsPerInch: 72`
and `PixelsPerInch: 144`, instead of `95` (`47.5 * 2`) at the latter.

A real fix needs the `PixelsPerPoint` catch-up to become a property of evaluating the calc AST itself
(threading it through `CalcContext`/`CalcEvaluator` alongside the already-threaded `emFactor`/`remFactor`,
or applying one multiply to the whole calc result when its category is a length) rather than something
gated on the whole input string being a single literal `Length` - with care to not double-scale a
percentage-only or viewport/container-relative-only calc expression, whose bases are already in the
inflated space. Deliberately left out of issue #826's fix to keep that change bounded to the literal,
non-calc box-geometry gap. Tracked as [#829](https://github.com/jhaygood86/PeachPDF/issues/829).
