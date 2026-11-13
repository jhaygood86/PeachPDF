# `width: min-content | max-content | fit-content` were ignored, and a wrapping flex column was as wide as its widest item

## The keywords
`AutoSizeConverter`/`SizeConverter`/`OptionalSizeConverter` accepted a length, a percentage, `auto`/`none`, `stretch` and `calc-size()`, but not the bare intrinsic keywords, so `width: max-content` was an invalid declaration and the box stayed `auto` (a block filled its container). `calc-size(max-content, size)` has always worked.

The bare keyword is the same size, so `CalcSizeValue.TryParse` now reads `min-content`/`max-content`/`fit-content` as the basis of an implicit `calc-size(<keyword>, size)` and every existing step (parking for measurement, `ResolveCalcSizeWidthsAsync`, the height resolution) applies unchanged; the three converters accept the keywords. On `height` and `min-`/`max-height` the keywords resolve as `auto`, as the calc-size path already does.

## The wrapping flex column
Found through css-flexbox `intrinsic-size/col-wrap-*` (all 14 failing): `width: max-content` on `flex-flow: column wrap` with a definite `height`. The intrinsic walk had a branch for flex rows only; a column fell to the flat walk and reported the widest item. `CssBox.TryMeasureFlexColumnWrapWidth` now breaks the items into columns at the container's content height and sums the column widths plus the column gaps. It needs every item's main size to be definite (a length `flex-basis`, or a `height`); otherwise it returns false and the old measurement stands.

## What the run showed
css +50 gained, 23 lost. The losses are tests that used a keyword on both the test and its reference and so passed while the keyword was ignored (both sides filled the container). They now depend on the intrinsic measurement being exact: children with a parked `calc-size()` width contribute their neutral size (`calc-size-min-max-sizes-003/-006`), letter-spacing at an inline's edge (`letter-spacing-205`), subgrid standalone-axis sizing (`standalone-axis-size-010..013`, and the grid-lanes lookalikes), `word-break: auto-phrase`, multicol `intrinsic-size-005`. Each is a separate intrinsic-measurement gap the keyword exposed; none was looked at here.

## Evidence
Net +27 passes, gate green, floors raised; `IntrinsicSizeKeywordTests` (5); the engine, source-generator, PeachPDF and PeachBrackets suites green.

## Not done
`min-height: min-content` and friends are `auto`-like by the calc-size path; `fit-content(<length>)` as a function is not accepted here.
