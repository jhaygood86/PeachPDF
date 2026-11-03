# `shape-outside` / `shape-margin` now change how text wraps around a float

Before, `shape-outside` and `shape-margin` were unknown properties: text always kept clear of the float's rectangle. Now a supported shape (`circle()`, `ellipse()`, `inset()`, `polygon()`, a shape-box) lets lines run into the corners the shape leaves free, as in browsers, so a document that declared `shape-outside` for a browser will wrap differently (and more like the browser) than before. `shape-outside: none` and unsupported values (`url()`, `path()`, an image) behave as before.
