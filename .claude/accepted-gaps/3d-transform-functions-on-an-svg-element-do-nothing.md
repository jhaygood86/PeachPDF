# 3D transform functions on an SVG element do nothing

CSS Transforms 2 §11 makes the 3D functions apply to SVG graphics elements, containers and `foreignObject`; here `SvgElement.Transform` is a `Matrix3x2`, so `translate3d()`, `matrix3d()`, `rotate3d()`, `rotateX/Y()` and `perspective()` contribute nothing and the rest of the list still applies (`SvgTransformParser`). The spec also asks that a list with a 3D function be ignored whole on `clipPath`, gradients and `pattern`.

A 2D projection of the affine part was considered and rejected: the spec asks for real 3D, and a projection would look right for `translate3d` and wrong for any rotation about X or Y. The fix is to give the SVG renderer the HTML pipeline's 3D path, tracked as #1807.
