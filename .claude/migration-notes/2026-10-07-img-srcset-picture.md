# `srcset`, `sizes` and `<picture>` now select the image

Before: `<img>` used only `src`; `srcset`/`sizes` and `<picture>`/`<source>` were ignored (a bare `<img srcset>` rendered blank).
Now: the highest-density matching candidate is loaded and laid out at its density-corrected size. Documents
that carried both `src` and `srcset` may now render a higher-resolution image, and at a smaller natural size
when the candidate has an `Nx`/`Nw` density above 1. `<source>` and `<track>` are void elements and `display: none`.
