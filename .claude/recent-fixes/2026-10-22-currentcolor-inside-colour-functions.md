# currentcolor inside a colour function (#1823, #1760)

The CSS-OM already keeps `color-mix(..., currentcolor, ...)` as text (its converter's argument grammar is `Any`), and the declaration was
dropped later, in the render layer: `CssValueParser.IsColorValid` (used by the property registry's `color` validation) and
`GetActualColor` rejected the `currentcolor` operand, so `background-color` kept its initial value.

What was done, deliberately without a second mixer:
- `CssValueParser.HasNestedCurrentColor` / `GetActualColor(text, currentColor)` replace the ident with `rgb(r g b / a)` and call the normal reader.
  `IsColorValid` treats the nested keyword as valid (it validates with a black in its place).
- Cascade-resolved properties (`background-color`, `outline-color`, `column-rule-color`) are rewritten in `CssUtils.ApplyCurrentColor`, in the
  comma `rgba()` form: the slash form was **not** accepted by the setter's validation (a found-by-running trap).
- Border sides stay unresolved in the cascade on purpose (see `DerivedStyle.ResolveBorderSideColor`); a nested keyword is read there per box.
- Gradient stops record `NestedCurrentColorStops` (index -> resolver closure over the parser) beside `CurrentColorStops`; `CssImagePainter.ResolveCurrentColorStops` applies it.
- A single colour stop parses (`linear-gradient(currentcolor)`, `linear-gradient(red)`): it is stored as that stop twice. Before, the render parser
  returned null for it, so the layer vanished and a `background: #fcc` fallback showed.

Not done: shadows (`box-shadow`/`text-shadow`) and `text-decoration-color` do not read a nested `currentcolor` yet; `-webkit-text-stroke-color`
likewise. The page-margin / canvas-propagation cases were split to their own issues.
