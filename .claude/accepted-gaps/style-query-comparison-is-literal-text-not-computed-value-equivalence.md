# `@container style()` compares resolved values as trimmed literal text, not computed-value equivalence

`StyleDeclarationCondition.Check` (the `style(<property>: <value>)` leaf, CSS Containment 3 §7.3)
compares the query container's resolved value for `<property>` against the queried `<value>` text via
a trimmed, ordinal string match. Neither side is normalized through the property's own computed-value
pipeline before comparing, so a query and a container can express the same computed value in
textually different forms and fail to match — e.g. `style(color: red)` does not match a container
whose resolved `color` serializes as `rgb(255, 0, 0)`, even though `red` and `rgb(255, 0, 0)` are the
same computed color.

This is spec-correct for the common cases: any custom-property (`--foo`) comparison (values are
opaque author-controlled text with no separate computed serialization to diverge from), and a
standard-property comparison whose authored value already matches PeachPDF's own canonical stored
form (e.g. keyword values like `style(display: block)`). It's incorrect for a standard property whose
authored and computed serializations genuinely differ. Tracked as
[#616](https://github.com/jhaygood86/PeachPDF/issues/616).
