# `isolation`, `background-blend-mode`, `color-scheme`, `light-dark()` and `print-color-adjust` now do something

They were unknown properties (ignored) before. Now:

- `isolation: isolate` makes the element a stacking context and an isolated group, so a descendant's `mix-blend-mode` blends inside it rather than with the page behind. A page that wrote it for a browser, with a blending descendant, now renders as the browser does.
- `background-blend-mode` blends each background layer with the ones below it and the colour, inside the element's own group.
- `color-scheme` sets the used scheme: `light-dark()` takes its second arm under a dark scheme, the system colours (`Canvas`, `CanvasText`...) take their dark values, an element with no `color` draws in `CanvasText`, and a page whose root is dark is drawn on the dark canvas colour. A page with `color-scheme: dark` (or `light dark` with `PreferredColorScheme` dark) used to render light. A `<meta name="color-scheme">` counts for a root that declares none.
- `light-dark()` was an invalid colour (the declaration was dropped); it is now a colour.
- The scheme-dependent system colour keywords (`Canvas`, `CanvasText`, `Field`...) are kept as keywords by the CSS-OM instead of being turned into `rgb()` at parse time, so `style.color` reads back `canvastext`.
- `print-color-adjust` (and `-webkit-print-color-adjust`, `color-adjust`) is accepted; every colour is drawn as authored under both values.
