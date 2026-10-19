# SVG style tiers: `!important`, invalid inline declarations, `currentColor` on a stop

#1779. Fixes in `SvgTreeBuilder.ResolveStyledAttr` and the inline `style=""` parser.

## What was done

- `SvgCssStyling.GetMatchedDeclarations` already ran a normal then an `!important` pass but dropped the importance; it now reports which winners are important (`ISvgSourceNode.IsMatchedCssDeclarationImportant`),
  so an important rule beats an inline declaration that is not important.
- `SvgValueParsers.ParseStyleDeclarations` strips `!important` (it used to leave it in the value, which read as black) and reports it, so an important inline declaration beats everything.
- `ResolveStyledAttr` takes an optional validity check: an inline value that fails it is dropped and the next tier answers. Only `stop-color` and `stop-opacity` pass one so far.
- `currentColor` on a gradient stop is the nearest `color` on the stop, the gradient or an ancestor in the SVG (`ResolveCurrentColor`), else the host colour.

## Not done

- Validity checks for other properties, and `initial`/`unset`/`calc()` in `style=""`: there is no generic inline-declaration validator, because `stop-color` and most SVG-only properties have no entry in `css-properties.json` yet (#1777).
- `currentColor` on `flood-color`/`lighting-color` still uses the host colour.
