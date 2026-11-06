# Motion path, and what the individual transform properties leave out

`translate`, `rotate` and `scale` (CSS Transforms 2 §3) compose in front of `transform` on HTML boxes and animate on their own (see
[the recent fix](../recent-fixes/2026-11-05-individual-transforms-composition-and-starting-style.md)). These are left out.

- **The motion path properties are not supported:** `offset-path`, `offset-distance`, `offset-rotate`, `offset-anchor`, `offset-position` and the `offset` shorthand. They
  would add one more step to the composition (translate, rotate, scale, *offset*, transform) and need a path sampler (the point and tangent at a distance along a
  `path()`, `ray()` or basic shape) that the engine does not have. Tracked as a missing CSS Motion Path 1 module; no issue is filed yet.
- **An SVG element ignores `translate`, `rotate` and `scale`.** The SVG builder reads `transform` through its own parser and `transform-origin`/`transform-box` through
  attributes; the individual properties would have to be composed there ahead of the parsed transform, and a percentage against a fill-box or stroke-box deferred with it.
- **Two rotations about different axes flip half way** (`rotate: x 90deg` against `rotate: y 90deg`). CSS Transforms 2 §3 mixes them through their matrices (a slerp); mixing the
  matrices as text would write `matrix3d()`, which `rotate` cannot read back, so a pair on one axis (the usual case, and `none` against any rotation) mixes the angle and the rest
  flips.
- **A percentage in `translate` is not resolved before layout**, so a keyframe animation of it is mixed on its own terms (`10%` to `50%` mixes, as a `transform` list does) but
  never as part of a matrix mix.
