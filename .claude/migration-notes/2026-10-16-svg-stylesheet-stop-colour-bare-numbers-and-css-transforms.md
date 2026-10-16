# SVG elements styled from a stylesheet: gradient stops, bare-number strokes, CSS transforms

What a rule in a stylesheet (or a `style=""`, or a `@keyframes` block) can say about an SVG element changes in three places, each a case where the value used to be dropped or misread
(`git show v0.9.22:src/PeachPDF/Svg/SvgTransformParser.cs` has no `translateX`, `StrokeWidthProperty` took no bare number and `stop-color` was not a property of the CSS-OM there). Each now matches
Chrome's computed style (Chrome 155.0.8059.40, a `rect` in a 200 x 100 `svg`):

- **`stop-color` and `stop-opacity` from a rule.** `stop { stop-color: #0000ff; stop-opacity: .5 }` was ignored (a stop read only its attributes and its own `style=""`); it now beats the presentation
  attribute, `style=""` beats it, `var()` and `currentcolor` (the colour of the host element) work. A gradient that relied on the attribute being the only thing that counted, with a rule that happened to
  match its stops, changes colour.
- **A bare number for `stroke-width` and `stroke-dashoffset`.** `stroke-width: 6` and `stroke-dashoffset: 7` (user units, SVG 2) were refused by the CSS-OM, so only `6px` worked and the
  usual line-drawing `stroke-dashoffset: 100` from a stylesheet did nothing; both are read now. A negative `stroke-width` (`-2`, `-2px`) is invalid as in Chrome, so the declaration is dropped.
- **The CSS syntax of `transform`.** A stylesheet `transform` was read with the attribute grammar, which ignores a unit: `rotate(0.25turn)` turned the element 0.25 degrees, `rotate(1.5708rad)` 1.5708 degrees,
  and `translateX(30px)`, `translateY()`, `scaleX()`, `scaleY()`, `skew()` were skipped. Angles now honour `deg`/`grad`/`rad`/`turn`, lengths `px`, `pt`, `pc`, `in`, `cm` and `mm`, and those functions apply.
  A function with an `em` or percentage length, a `calc()` or a 3D function is still skipped, and `transform-origin` is still not applied.
