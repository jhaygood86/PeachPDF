# Bevelled borders and outlines shade by luminance, matching current Chrome

`inset`, `outset`, `groove` and `ridge` — on borders, outlines and collapsed table segments alike —
pick their two face colors by a different rule than in v0.9.19. Two kinds of declared color paint
differently now; everything else is byte-identical.

## Near-white colors keep the declared color on the lit face

Before, the lit face was always `Light(color)`, which for a near-white color clips to `#ffffff` and
erases the distinction the bevel is made of. Now, above the relative luminance of `rgb(235, 235, 235)`
the lit face is the declared color itself.

```html
<div style="width:200px;height:60px;border:12px outset #f0f0f0;background:#888"></div>
```

| face | v0.9.19 | now | Chrome |
| --- | --- | --- | --- |
| lit (top/left) | `#ffffff` | `#f0f0f0` | `#f0f0f0` |
| dark (bottom/right) | `#9c9c9c` | `#9c9c9c` | `#9c9c9c` |

The threshold is strict, so a gray of 235 still lightens and a gray of 236 does not.

## Some very dark chromatic colors now darken instead of lightening

The "too dark to darken, so lighten both faces" fallback used to trigger on a WCAG contrast ratio
below 1.3 between the color and its own darkened form. It now triggers at or below the relative
luminance of `rgb(32, 32, 32)`, which is what Blink actually tests. The two agree on every gray — 32
lightens, 33 darkens, as before — but disagree on 23,328 chromatic colors in the very dark corner of
sRGB, where the old rule lightened and Chrome darkens:

| color | v0.9.19 dark face | now | Chrome |
| --- | --- | --- | --- |
| `#001e4c` | `#003fa0` (both faces lightened) | `#000000` | `#000000` |

So a `border: inset #001e4c` that used to paint as two light blue faces now paints as a black face
and a light blue one, the same as a browser.

## Why

Both are the same rule: Blink's `CalculateInsetOutsetColor` under the
`TableDefaultBorderColorCurrentColor` feature, stable since M151. PeachPDF reproduces Chrome's bevel
shading deliberately (down to Blink's integer truncation and its 255.99998 scale factor), so the whole
function was ported rather than only the near-white half. CSS 2.1 §8.5.3 leaves the exact shading
UA-defined, so neither the old nor the new behavior is a spec violation — but only one of them matches
the browser a reader is comparing against.
