# `clip-path`: `<geometry-box>`, `inset()` rounding, and `url(#id)` now render

Three previously-inert or previously-rejected `clip-path` value shapes now render:

1. **`inset(... round <border-radius>)`.** Previously parsed but rendered as a plain sharp-cornered
   rectangle - the `round` clause was accepted but silently ignored. It now actually rounds the
   corners, with the same 1–4-value/`/`-split grammar and corner-overlap reduction `border-radius`
   itself uses. A document that already wrote `inset(... round ...)` expecting no visual effect will
   now see rounded corners.

2. **A `<geometry-box>` keyword** (`content-box`, `padding-box`, `margin-box`, `fill-box`,
   `stroke-box`, `view-box`) alongside or instead of a basic-shape function. Previously this made the
   *entire* `clip-path` declaration invalid (dropped to the initial `none`, i.e. no clipping at all) -
   `clip-path: circle(50%) padding-box` clipped nothing. It now parses and resolves against the
   selected box. Any document that unknowingly wrote a geometry-box keyword into `clip-path` (perhaps
   copied from other CSS, or written expecting standard behavior) previously got no clip at all and
   will now actually get one.

3. **`url(#id)`** referencing an SVG `<clipPath>` element defined anywhere in the document (including
   inside a `<svg style="display:none">` used purely as a defs resource). Previously invalid
   (dropped, no clip). Now resolves and clips to that `<clipPath>`'s geometry.

Additionally, `inset(... round <invalid-token>)` (e.g. `inset(10px round banana)`) previously
parsed successfully (rendering a sharp rectangle, the same as no `round` at all) and now correctly
invalidates the whole `clip-path` declaration per spec - a document relying on that leniency will now
see no clipping instead of a sharp-rectangle fallback.

Tracked as [issue #217](https://github.com/jhaygood86/PeachPDF/issues/217).
