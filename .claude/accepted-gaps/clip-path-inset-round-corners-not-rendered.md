# `clip-path: inset()`'s `round <border-radius>` corners aren't rendered

`inset(... round <radius>)` ([CSS Shapes Level 1](https://www.w3.org/TR/css-shapes-1/#funcdef-inset))
is parsed and the radius tokens are captured (`BasicShapeGrammar.ParsedBasicShape.InsetRoundRadius`),
but `CssClipPathResolver.BuildInset` renders the clip as a plain rectangle - the corner radius is
never applied, so `clip-path: inset(10% round 20px)` clips to a sharp-cornered rectangle instead of
a rounded one. Filed as [issue #217](https://github.com/jhaygood86/PeachPDF/issues/217).
