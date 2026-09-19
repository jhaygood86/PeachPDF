# A bevel's lit face is lightened, and current Chrome agrees

A change on this branch replaced `BorderBevelColors.Shade`'s lit face - `Light(color)` - with the
declared color untouched, on the evidence of a single sampled Chrome `groove` in `#4a90d9`. It was
dropped again before merge. Recording why, because the sample that motivated it is reproducible and
the conclusion drawn from it is not.

## What the measurement actually says

Blink's `CalculateInsetOutsetColor` (`box_border_painter.cc`) shipped a new rule in M149 (updated in
M151, feature `TableDefaultBorderColorCurrentColor`, now stable): the dark face is `Dark()`, and the
lit face is the **raw color only when its relative luminance is above 0.83077** - otherwise `Light()`.
Very dark colors still lighten both faces.

Measured against Edge 153 on `inset`/`outset`/`groove`/`ridge`, borders and outlines alike:

| color | Chrome's lit face | `Light(color)` | raw color |
| --- | --- | --- | --- |
| `#4a90d9` | `#57a9ff` | match | wrong |
| `#808080` | `#d4d4d4` | match | wrong |
| `rgba(74,144,217,.5)` over white | `#aad3ff` | match | wrong |
| `#f0f0f0` | `#f0f0f0` | wrong (`#ffffff`) | match |

Over 48 random colors plus 12 greys, `Light()` matches Chrome's lit face exactly in 58 of 60 cases and
the raw color in 7. The dark face is identical under either rule. So the `#4a90d9` sample that started
this reads as "the raw color" only if the darkened face is mistaken for the lit one; every
non-near-white color is genuinely lightened.

## What is left open

Tracked as jhaygood86/PeachPDF#1224. PeachPDF is wrong for the near-white end: `Light(#f0f0f0)`
clips to `#ffffff` where Chrome keeps `#f0f0f0`. Closing that means porting Blink's actual rule - raw color above 0.83077 relative
luminance, `Light()` below - which should take the exact-match rate to 60/60. It is its own change,
not a side effect of an outline-union PR: `BorderBevelColors` is shared by every bevelled border,
outline and collapsed-table segment, and the lightening it does today shipped in v0.9.19.

## Traps

- A bevel sample has to be read as a *pair*. Both faces move away from the declared color in most
  cases, so a single face sampled on its own can be matched to the wrong rule.
- `docs/html-css-support.md` documents the lightening ("`inset` darkens the top and left and lightens
  the bottom and right"). Changing `Shade` without that line is how the docs came to contradict the
  code for the length of a review round.
