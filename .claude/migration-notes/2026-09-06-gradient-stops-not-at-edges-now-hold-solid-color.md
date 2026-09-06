# Gradient stops not at 0%/100% now hold solid color outside their range, instead of the whole gradient stretching to fit

Previously: a `linear-gradient()`/`radial-gradient()` whose first color stop wasn't at position 0%
and/or whose last color stop wasn't at 100% rendered as if those stops *were* at the domain's
edges — the color transition stretched to fill the entire gradient box. For example,
`linear-gradient(red 20%, blue 80%)` rendered identically to `linear-gradient(red, blue)`: a smooth
red-to-blue fade across the whole box, instead of solid red for the first 20%, a fade from 20%-80%,
and solid blue for the last 20% (the CSS Images 4 §3.5.5-correct result). The same applied to a
gradient's alpha channel when its colors carried varying opacity.

This was most visible for a gradient whose first and last stop share the *same* position — a common
"hairline" idiom (`linear-gradient(color <width>, transparent <width>)` tiled via `background-size`
to draw a crisp line without extra markup, used by libraries like Charts.css for chart gridlines):
it rendered as a soft wash spanning the whole background tile instead of a crisp line.

Now: colors hold solid outside the first/last stop's position, matching every other renderer. A
document that was relying on the old (incorrect) stretch-to-fit behavior — intentionally or not —
will see its gradient's visible color transition become narrower, confined to the actual
first-stop-to-last-stop range instead of spanning the whole gradient box.
