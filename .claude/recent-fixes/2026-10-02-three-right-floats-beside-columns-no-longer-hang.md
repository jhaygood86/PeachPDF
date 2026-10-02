# Three floats placed side by side no longer spin the placement loop

Three or more `float: right` boxes beside a multi-column container made `IsFloatIntersecting` answer "intersects" for a candidate that sat past the containing block's right edge (CSS 2.1 9.5.1 rule 9 lets a float extend out to meet an earlier one) every time the placement loop retried, so the loop moved the candidate left by one float, was pushed back right, and never ended. Found by the wide corpus sweep (seed 540, CPU spin, stack via dotnet-dump).

The first right-float case now applies only when the candidate lies past the containing block's right edge *and* is not inside the multicol container's own range (`MulticolRight`); a candidate that is inside it is judged by the ordinary overlap test, which terminates.

A first attempt added a placement-only flag; it broke `FloatRight_InNarrowerNestedBlock...AvoidsAWiderAncestorFloatRightSibling`, because that case needs the old behaviour for an ancestor's float. Keying on `ContainingRight` keeps it.

Test: `RightFloatsSideBySideTests` (both documents time out on main). Suite green on Windows net8.0.