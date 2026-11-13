# `width: min-content | max-content | fit-content` now size the box to its content

Before: on `width`, `min-width` and `max-width` these three keywords were invalid and dropped, so the box kept its `auto` size (a block filled its container, and the keywords did nothing in a flex or grid item either). `calc-size(max-content, size)` worked.
Now: the keywords work, as in a browser: `width: max-content` is as wide as the unwrapped content, `min-content` as the widest unbreakable run, `fit-content` is `min(max-content, max(min-content, available))`. A page that carried one of them by accident, or that relied on a block filling its container, can change.
A wrapping `flex-direction: column` container with a definite `height` is as wide as its columns side by side when its width is intrinsic.
