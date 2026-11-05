# `image-set()` is validated, not rendered

`image-set()` in a CSS `<image>` value (`background-image`, `list-style-image`, `content`) parses and is accepted by `@property`
registration, but the property that holds it paints nothing for the function itself: only `url()` and gradients render.

**Why left**: the responsive-image work gave `<img srcset>` an explicit selector (`ResponsiveImageSelector`) with a screen policy,
and `image-set()` can reuse it (candidates are `x` resolutions with an optional `type()`), but it also needs the CSS-OM value to
carry the candidates through to the background and list-marker image resolvers, which is a change to value resolution and not to
the selector.

**When picking this up**: build the candidate list from the `image-set()` options, rank it with `ResponsiveImageSelector`'s
ranking for the container's `ImageSelection`, and resolve the chosen URL where `url()` is resolved today.

Tracking issue: #2197.
