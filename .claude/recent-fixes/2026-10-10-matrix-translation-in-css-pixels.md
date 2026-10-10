# matrix()/matrix3d() lengths converted from CSS pixels to layout points (#1692)

`CssValueParser.BuildFunctionMatrix` fed the raw numbers of `matrix()`'s `e`/`f` and `matrix3d()`'s 16 values straight into the `Matrix4x4`, whose lengths are layout points, so a translation was 1/0.75 = 33% too large. The values are CSS pixels (css-transforms-1), the unit `translate()` resolves through `ParseLength`. Two groups of `matrix3d()` entries carry a length, in opposite directions:

- the translation (13th to 15th values) is a length: multiplied by `Length.PointsPerPx`;
- the perspective terms (4th, 8th, 12th: `w' = x*m14 + y*m24 + z*m34 + 1`) are a number *per length*: divided by `Length.PointsPerPx`. Without this, `matrix3d(..., 0,0,-1/300,1)` was not `perspective(300px)` (whose `M34` is `-1/225` per point); it foreshortened 25% too weakly (the right strength is 33% above the old one).

The linear 3x3 part is unitless and untouched. Both functions build their raw matrix and pass it through `PixelMatrixToPoints`, which is just `S⁻¹ · M · S` with `S = scale(pt/px)`: the two groups above fall out of it, so a new function that builds a matrix from raw pixel numbers uses the same helper rather than scaling entries by hand.

Tests (`TransformIntegrationTests`) assert against the equivalent `translate()`/`translate3d()`/`perspective(300px)`. `ActualTransformMatrix` is a `Matrix3x2` and drops z, so the z-translation and perspective terms are read from `ActualTransform4`.

`Matrix3dPerspectiveRasterTests` rasterizes `perspective(300px) rotateY(40deg)` and its `matrix3d()` spelling and compares pixels (726 differ without the fix, tolerance 40), because the entry-level assertions say nothing about what the projective warp draws.

Evidence: `--filter Transform` on net8.0, 410 passed. Rasterized a page of `matrix(1,.2,.3,1,40,20)`, `matrix3d` translation and `matrix3d` perspective terms against headless Chrome's own PDF of the same HTML, through PDFium: the shapes agree to within a pixel at 2x (Chrome's page is 0.08pt wider), and before the fix the translated boxes sat 15pt too far right/down.
