# `font-weight: bolder`/`lighter` is resolved once, on the declaring element (#2137)

**Bug:** `FontArea` is adopted by reference from the parent, so a text box under `<b>` inherited the
*keyword* `bolder` and `DerivedStyle.ActualNumericWeight` stepped it again (text in `<b>` = 900,
`<b><b>` text = 1000).

**Fix:** `DomParser.ResolveRelativeFontWeight` runs at the end of each box's cascade (after
`ApplyCurrentColor`, before the shared-style capture) and rewrites a `bolder`/`lighter` computed value to
the number `FontWeightResolver` gives against the parent's `ActualNumericWeight`. Descendants then inherit a
number (CSS Fonts 4 §2.2.1: computed value is a number). Blink does the same in
`StyleBuilderConverter::ConvertFontWeight`, resolving against the parent's weight when the declaration is applied.

**Trap:** a computed `font-weight` read back through the box (`box.FontWeight`) is now numeric for
`bolder`/`lighter`; `bold`/`normal` keep their keyword. Tests that pinned the stored keyword were updated.
Resolving in `ActualNumericWeight` by comparing against the parent's area does not work: area sharing and
the style-transition cache make "inherited" and "declared the same value" indistinguishable.
