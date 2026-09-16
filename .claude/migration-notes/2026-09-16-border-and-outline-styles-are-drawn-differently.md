# Border and outline styles are drawn differently

Every `border-style`/`outline-style` keyword other than `none`/`hidden`/`solid` now renders to match
what a browser produces. Documents using them will look different — in every case closer to how the
same markup previews in a browser.

- **`dotted`** drew square dots on a fixed period that was cut off wherever the edge ended. It now
  draws round dots whose diameter is the border width, on a period stretched or squeezed so a whole
  number of dots spans the edge exactly. **`dashed`** used a dash three times the border width on the
  same fixed period; it now uses a dash twice the border width, fitted the same way. Because the fit
  depends on the edge, spacing shifts slightly as a box's width or height changes, and a box's
  horizontal and vertical runs may use slightly different gaps. At a square corner where a
  dotted/dashed edge meets an edge with a different style, color, or width, its final mark is clipped
  to the corner transition diagonal rather than showing through the adjoining border or ending on a
  square cut.
- **`double`**, **`groove`** and **`ridge`** drew each edge as two stripes running the full length of
  the box, so the top edge's inner stripe crossed the left and right borders' gaps and all four
  corners showed a ladder of crossing lines. Each line is now mitred into its neighbours, giving clean
  nested rectangles.
- **`double`**'s two lines were `floor(border-width / 3)` each, which made the gap wider than either
  line at most widths. They are now exact thirds.
- **`groove`** and **`ridge`** shaded all four sides alike, which produced a flat two-tone frame with
  no bevel. `groove` now draws its outer half as `inset` and its inner half as `outset`, and `ridge`
  the reverse — and since those shade per side, the border now reads as carved or raised.
- **`inset`** and **`outset`** darkened one pair of sides by halving each color channel and left the
  other pair at the declared color. The darkened pair is now a different, browser-matching shade, and
  the other pair is genuinely lightened rather than left alone. A color too dark to darken visibly —
  black most importantly, which is the initial `border-color` via `currentColor` — now lightens both
  faces by differing amounts instead, so a `border: 2px inset black` shows a visible bevel where it
  previously painted black on black.

- A border whose four sides share a style and color previously left a pale hairline of background down
  each corner's mitre diagonal, because two adjacent painted edges did not quite meet. Such a border
  is now painted as one closed ring and the seams are gone.
- A **rounded** border was stroked using the box's own corner radii rather than the smaller radii its
  centreline actually has. A radius large enough to round a box fully — a pill, `border-radius: 999px`
  — made the arcs overlap, and a short stub appeared sticking out of the middle of each end. Pills and
  other large radii now render correctly.
- A **rounded** border whose four sides share a style, color and width is now stroked as one
  continuous outline. Previously each of the four edges was stroked separately, which left a faint
  mark where they met, and — for `dotted` and `dashed` — restarted the pattern four times, piling dots
  two and three deep at the corners. The pattern is now fitted to the whole perimeter and runs evenly
  the whole way round.

- A **rounded** `double`, `groove`, or `ridge` border previously rendered as a single solid line at
  the full border width. When its four sides share a style, color and width, `double` is now drawn as
  two concentric rounded lines at the thirds, while `groove` and `ridge` retain their two curved,
  shaded half-width bands.

`outline-style` uses the same rendering throughout, so all of the above applies to outlines as well,
and to the borders of a `border-collapse: collapse` table.

`text-decoration-style: dotted`/`dashed` is unchanged.
