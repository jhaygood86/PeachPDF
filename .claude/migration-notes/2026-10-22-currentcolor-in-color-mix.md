# currentcolor inside color-mix() and a single-stop gradient

`background-color: color-mix(in srgb, currentcolor, red)` (and the same in border, outline, column-rule colours and gradient stops) used to be
dropped, leaving the property at its initial value (a stop painted black). It now mixes with the element's `color`.

`linear-gradient(red)` / `linear-gradient(currentcolor)` (one colour stop) used to paint nothing, so a `background` fallback showed; it now paints that colour solid, as a browser does.
