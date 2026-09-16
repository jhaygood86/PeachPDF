# `text-decoration-style: wavy` renders a wave

## What changed

`text-decoration: underline wavy` (and `overline`/`line-through wavy`) used to render identically to
`solid` — a straight line — because there was no wave primitive in the rendering layer. It now strokes
an actual wavy curve, matching what `wavy` has always meant in browsers.

A document that declared `wavy` expecting the browser-familiar squiggle (a spell-check-style
annotation, most commonly) previously got a plain straight line in the generated PDF with no visual
indication anything was wrong. It now gets a real wave. If your document relied on `wavy` rendering as
a straight line — unlikely, since that was never the intended behavior — switch it to `solid` explicitly.

The wave's centerline sits about one resolved `text-decoration-thickness` away from where a `solid`
line of the same decoration would sit, growing away from the text the same direction `double`'s second
stroke already does (downward for an underline/line-through, upward for an overline) — so a
newly-wavy underline sits slightly lower than the equivalent solid one used to.

`text-decoration-skip-ink` (the default) still breaks a wavy underline/overline around glyph
descenders, the same as it does for every other style.

## Not affected

SVG `<text>` decorations: `double` there is unchanged (still a single stroke, a separate pre-existing
limitation) — but SVG's `wavy` picks up the same real wave HTML now renders.
