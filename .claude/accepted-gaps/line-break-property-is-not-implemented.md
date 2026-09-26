# The `line-break` property is not implemented

CSS `line-break` (`auto | loose | normal | strict | anywhere`) is not in `css-properties.json`: it is not parsed and has no effect.
Line breaking always uses the strictness of `normal`, although `PeachDrawing.Text`'s `LineBreaker` already implements all five
values (`LineBreakOptions.Strictness`) and `UnicodeLineBreaks.Find` is the one place that would pass it. Adding the property is
the enum, the keyword map, the `css-properties.json` entry, the box property, `@supports`, docs and tests. Tracked in
[#1404](https://github.com/jhaygood86/PeachPDF/issues/1404).
