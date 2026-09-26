# Variable fonts: `@font-face` ranges and `font-stretch` percentages

`@font-face` descriptors are single values: `font-weight: 100 900` registers the face at its lower bound, and `font-stretch: 75% 125%` or
`font-style: oblique 0deg 14deg` are not carried as ranges. The `font-stretch` property accepts the nine keywords (mapped to 50% to 200%
of the `wdth` axis) but not a percentage. Tracked in [#1410](https://github.com/jhaygood86/PeachPDF/issues/1410).

What makes this tolerable: a variable face is matched at the weight, width and slant of the requesting box through the axes of its own
`fvar` table (`VariableMatching`), so one `@font-face` rule for a variable file serves every weight. What is lost is choosing between
several `@font-face` faces of one family by their declared ranges, and asking for a width between two keywords.

Fixing it means `FontFaceDescriptorResolver`, `FontResolver.AddFont` and `TryFindNearestFace` carrying ranges instead of integers, and
the width in `TypefaceQuery` becoming a percentage instead of an OpenType class.
