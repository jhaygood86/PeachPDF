# Rounded `groove`/`ridge` falls back when another visible side uses a different style

If any visible side of a rounded box uses a line style outside the `groove`/`ridge` family—such as
`solid`, `double`, `dotted`, or `dashed`—the dedicated rounded bevel path cannot handle the box.
Every `groove`/`ridge` side on that box then paints through the general per-edge path as one solid
stroke at its full width, losing its two-tone bevel. This is a box-wide dispatch limitation, so even
a `groove` edge not directly adjacent to the different-style side falls back.

A box whose visible sides are all `groove`/`ridge` is implemented fully: widths and colors may differ
per side, `groove` and `ridge` may be mixed, and a sliced fragment may omit physical edges. CSS 2.1
§8.5.3 defines both styles' appearance as UA-dependent, but "renders as a bevel" is clearly the
intent and every browser draws one, so the remaining mixed-style fallback is a genuine deviation
rather than a permitted choice.

**No tracking issue can be filed while this repository has GitHub Issues disabled.** An attempt to
create one on 2026-09-16 was rejected by GitHub for that reason; file and reference it here if the
tracker is enabled later.

## Reproduction

Open this document in Chrome and render the same source through PeachPDF. Chrome keeps the top
`groove` and bottom `ridge` as two shaded bands; PeachPDF draws each as one full-width blue stroke
because the right `solid` and left `dashed` sides keep the box off the dedicated bevel path.

```html
<!doctype html>
<style>
  @page { size: 500px 260px; margin: 0 }
  html, body {
    width: 500px;
    height: 260px;
    margin: 0;
  }
  body {
    box-sizing: border-box;
    padding: 28px;
    background: white;
    font: 16px Arial, sans-serif;
  }
  .sample {
    box-sizing: border-box;
    width: 444px;
    height: 160px;
    margin-top: 16px;
    border: 18px #4a90d9;
    border-style: groove solid ridge dashed;
    border-radius: 36px;
    background: #eee;
  }
</style>
<strong>Mixed rounded styles</strong>
<div class="sample"></div>
```

## Why

The general mixed-style rounded-border path paints each edge as a **stroke** along a curve, and a pen
has one width and one color: it draws exactly one band, centered on its path.

The dedicated `groove`/`ridge` path avoids that restriction by filling separately-colored rounded
edge bands. It groups same-colored adjoining sides into one path so their shared corner has no
antialiasing seam, uses each side's own width for both contour insets, and leaves square open ends
where a sliced fragment omits an edge.

It cannot take a corner shared with another style without also replacing that style's rounded-edge
rendering: both edges need to agree on the same transition points and band structure.

## What a real implementation would need

General rounded edge bands for every border style, so both sides of a mixed-style corner share one
transition geometry. Reusing the bevel builder for only the `groove`/`ridge` edge would leave two
problems:

- Independently painted pieces that only abut at the transition can leave an antialiasing seam (see
  [the abutting-fills invariant](../invariants/paint-two-abutting-antialiased-fills-leave-a-seam-along-their-shared-edge.md)),
  while overlapping them hides the seam by double-compositing translucent borders.
- `GetRoundedBorderPath` gives **both** corner arcs to the top and bottom edges, while the beveled
  band builder divides each arc according to the adjoining widths. A mixed-style corner needs the
  other style to use that same division rather than continuing to assign the complete arc to one edge.

So the remaining mixed-style case is not a local `groove`/`ridge` change and stays out of scope.

## Do not confuse with

A `double` border whose four sides do **not** share a style, color and width also falls back, for the
same one-pen-one-band reason. The rounded band builder now exists for `groove`/`ridge`; supporting
non-uniform `double` would require extending its band model to represent the empty middle third and
then sharing that geometry with every adjoining style at mixed corners.
