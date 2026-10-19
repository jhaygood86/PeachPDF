# A `url("#id")` reference from a stylesheet or `style=""` resolved to no id

#1776. `.band { fill: url(#sky) }` on an inline `<svg>` rect drew nothing; `fill="url(#sky)"` drew the gradient.

## What was found by running it

The issue's description (a band drawn far right, a second rect missing) did not reproduce on `main` as stated; what did was the class-rule rect drawing nothing at all. Probing `CssParser.ParseStyleSheet` showed the
CSS-OM serialises the value as `url("#sky")`, with quotes, where the attribute is raw. Three places pulled the id out with `IndexOf('#')`..`IndexOf(')')` (`SvgValueParsers.TryParsePaint`, `ParseMarkerReference`, the
`clip-path` read in `SvgTreeBuilder.ApplyCommon`), so the id came out as `sky"` and found no gradient. The same applied to `clip-path`, `mask` and `filter` written with quotes.

## What was done

One shared `SvgValueParsers.TryParseUrlFragment` that strips the quotes, used by all three. Tests: `SvgQuotedUrlReferenceTests`. The `svg_stylesheet_properties` showcase now fills its band from a rule.

## Not done

`mask`, `marker-*` and the other SVG-only properties are still dropped from a stylesheet (#1777), so quoted `mask`/`marker` from a rule is only reachable through `style=""`.
