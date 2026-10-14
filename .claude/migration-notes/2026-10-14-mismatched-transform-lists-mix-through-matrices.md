# Mismatched `transform` lists in a CSS animation are mixed instead of flipping

With `PdfGenerateConfig.AnimationProgress` set, two `transform` values that differ in their functions, or in how many
functions or arguments they have (`rotate(0deg)` against `translateX(100px) scale(2)`, `scale(1)` against `scale(2, .5)`,
`rotate(0deg)` against `rotateZ(270deg)`), used to show the first keyframe's value until half way through the interval
and the second's after. They are now mixed as CSS Transforms 1 §11-13 describes: functions are paired position by position
(the shorter list continuing with identity functions, `translateX` pairing with `translate`, `rotate` with `rotateZ`) and each
pair is mixed on its own, so `rotate(0deg)` against `rotate(360deg) scale(1.2)` turns a full time; what is left after
the first position with no common primitive is composed to one matrix per list and mixed through the matrices'
decompositions, where a rotation takes the short way round. `matrix()` and `matrix3d()` pairs that used to
mix entry by entry now mix as matrices (a quarter turn written as `matrix()` passes through an eighth of a turn, not a
shrunk box), `rotate3d()` pairs with different axes mix as matrices too, `none` against a `matrix()` list mixes (it used to
flip), and a matrix that cannot be inverted (`matrix(1, 0, 0, 0, 0, 0)`) now flips half way instead of mixing entry by entry.

A matrix part with a percentage argument, a `perspective()` against another function, or a collapsed scale (`scale(0)`) still flips half way.

Before this change (`git show v0.9.22:docs/html-css-support.md`) the docs listed these pairs as flipping half way.
