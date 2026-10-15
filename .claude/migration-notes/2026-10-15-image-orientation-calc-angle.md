# `image-orientation` accepts a `calc()` angle

`image-orientation: calc(45deg + 45deg)` (also `calc(1turn / 4) flip`) used to be dropped as invalid, so the image kept
its previous orientation. The math function is now evaluated and rounded to the nearest quarter turn like a plain angle.

Before this change (`git show v0.9.22:src/PeachPDF/CSS/ImageOrientation.cs`) the grammar accepted only an `<angle>` dimension token.
