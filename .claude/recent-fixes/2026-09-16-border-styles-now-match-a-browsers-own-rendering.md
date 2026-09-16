# Border styles are drawn the way a browser draws them

`dotted`, `dashed`, `double`, `groove`, `ridge`, `inset` and `outset` all rendered visibly unlike
Chrome. Five separate defects, one code path.

**Square dots, ragged spacing.** `BordersDrawHandler.GetPen` mapped `Dotted` to `RDashStyle.Dot`,
which `PdfGraphicsState.RealizePen` writes as `[w] 0 d` under the pen's default butt cap - square
dots, on a fixed period that simply got truncated at the far corner. `Dashed` was worse: PDFsharp's
canned `Dash` is `[3w w]`, but a browser uses a dash of `2w` and an ideal gap of `w`.

**The period is fitted, not fixed.** The rule (Blink's `SelectBestDashGap`, now
`StyledStrokeFitting`): take the two whole dash counts bracketing the edge, compute the gap each
would need, keep whichever is closer to the ideal. It is *not* "round up" - a 204-long edge with a
2-wide dashed border takes the smaller count, and there is a regression test pinning exactly that,
because an implementation that always rounds up passes every other case.

**The ladder at every corner.** `DrawDoubleOrGrooveRidgeBorder` stroked each edge as two lines
running the full `rect.Left`→`rect.Right`, so the top edge's inner stripe ran straight through the
left and right borders' gap. All four corners became a visible grid. Fixed by generalizing
`SetInOutsetRectanglePoints` into `SetBandPoints`, which takes a `from`/`to` fraction of the edge's
width: a point at fraction `f` sits `f` of the *adjacent* edge's width in from the box side, which is
the corner's true mitre line, and stays correct for unequal per-side widths where the mitre is not
45°. `double` is then bands `[0, 1/3]` and `[2/3, 1]`; `groove`/`ridge` are `[0, 1/2]` and
`[1/2, 1]`. The old `Math.Max(1, Math.Floor(width / 3))` was also wrong - a browser uses exact
thirds, which was confirmed down to 1px borders.

**groove/ridge were flat.** The two stripes picked their colors without regard to which edge they
were on, so all four sides shaded alike. A browser paints `groove`'s outer half as `inset` and its
inner half as `outset` (`ridge` the reverse), and inset/outset shade *per side* - top/left one way,
bottom/right the other. That per-side flip is the entire 3D effect.

**The bevel colors themselves.** `Darken` was `c / 2`. The real transforms scale every channel so the
brightest one moves by 0.33, and the lit face is genuinely *lightened*, not left at the declared
color. `BorderBevelColors.Dark`/`Light` reproduce Chrome's output byte-for-byte, including the
truncating cast and the 255.99998 channel scale - drop either and results land one off.

The non-obvious part is the fallback: a color at or near black cannot be darkened into a visible
edge, so both faces lighten instead, one step apart. The trigger is a *contrast* test, not an "is it
black" test, and the threshold was measured by bisection rather than guessed - gray 32 (ratio 1.289)
lightens, gray 33 (ratio 1.304) darkens, so the constant is 1.3. This matters more than it looks:
`border: 2px inset black` is what a UA-default `<fieldset>`/`<table border=1>` amounts to, and
without the fallback it paints black on black.

## Everything here was measured, not recalled

Every constant above came from screenshotting the same HTML in headless Chrome
(`--force-device-scale-factor=3`, so the raster is exactly 3 device px per CSS px - which PeachPDF's
own PDF also is at `scale=4`, since 1px = 0.75pt) and reading colour runs off scanlines. That is
worth repeating for any future change here: the first pass at this reconstructed `Color::Dark` from
memory as a flat `2/3` multiply, which fits nothing, and inferred a contrast threshold of 1.25, which
is off by one gray step. Both were wrong in ways that only pixel measurement caught.

The verification is the same trick in reverse: locate each swatch box in *its own* raster (the two
engines lay the page out a hair differently, so a shared coordinate grid drifts and silently compares
white space), then diff the colour runs relative to that box. Bevel colours now match Chrome exactly;
geometry matches to sub-pixel.

## PDFsharp's dash guard had to be relaxed

A dot is a zero-length dash under a round cap - the renderer paints a filled circle of the pen's
width. `XPen.DashPattern` rejected any element `<= 0`, which is stricter than PDF 32000-1 §8.4.3.6
("nonnegative ... shall not all be zero") and made a true round dot inexpressible. The setter now
enforces the spec's actual constraints. Verified in both PDFium and MuPDF before relying on it.

One trap found by running it: a zero-length dash landing *exactly* on the path's endpoint is a coin
flip - PDFium drops it, MuPDF keeps it. `StyledStrokeFitting.Apply` therefore runs the dot path half
a period past the final dot, which cannot introduce a spurious dot and guarantees the last one.

## Two more found by actually looking at the output

Both surfaced only after adding showcase cases for mixed widths and rounded corners — neither was
visible in the original swatches, and no test would have caught either.

**Pale seams down every mitre.** Two antialiased polygons that share an exact edge do not composite to
full coverage, so a plain `border: 4px solid` showed a hairline of background down all four corner
diagonals. Pre-existing for `solid`, and this change would have extended it to `double`/`groove`/
`ridge`, which now paint as polygons too. `TryDrawUniformBorder` paints a border whose four sides
share a style and color as one closed ring per band instead — two rectangular subpaths filled
even-odd, so every pixel is painted exactly once. Painting *once* rather than overlapping two rects
matters: a translucent border color or an active blend mode would show a doubly-painted corner.

Only `solid` and `double` qualify. The beveled styles shade each side differently by design, so they
have no single color to fill a ring with — and being multi-colored, they are exactly the cases where
the seam does not show.

This changes the draw-call shape for the commonest border there is, which broke 12 tests across five
files that were counting polygons as a proxy for "the border painted". They were updated to read
`TestRecordingGraphics.FilledShapes`, which reports a fill whichever primitive produced it. Two
`box-decoration-break` tests needed real rethinking rather than a swap: they counted per-edge quads to
prove each fragment closes itself, and a closed fragment is now *one ring*, which is a cleaner
observable for the same property — hence `CountBlueRings`.

**A pill's ends grew stubs.** `GetRoundedBorderPath` strokes down the middle of the border but was
using the border box's own corner radii, not the centreline's. The centreline radius is smaller by
half the border width on each axis (X follows the left/right border, Y the top/bottom), so every
straight run started half a width too far along. Harmless while the radius is large relative to the
border - which is why it survived this long - but once a radius reaches half the box, i.e. a pill, the
two arcs claim more than the centreline has and the run between them comes out **reversed**; the pen
then paints that backwards segment as a stub poking out of the middle of each end cap. The radii are
now reduced, floored at zero.

Flooring at zero would have dropped a thick-border/small-radius box out of the rounded path entirely
(all four reduced radii zero → null path → back to mitred quads), losing its rounding *and* regaining
the corner seams. So whether an edge gets a path is still decided by the box's **own** radii; only the
arcs themselves use the reduced ones, which degrades to a square centreline stroke rather than to four
quads.

**Dots piling up at a rounded corner.** `GetRoundedBorderPath` builds a separate path per edge, and
the top edge's path carries both corner arcs while the sides are straight lines. A dash pattern
restarts its phase at each, so dots landed two and three deep where an arc handed over to a straight
run — barely visible with the old square dots, obvious once they became circles.
`TryDrawUniformRoundedOutline` strokes a uniform rounded border as one closed outline, with the
pattern fitted to its whole perimeter. Solid is in there for the seam rather than the phase: four
strokes butting end-to-end leave the same pale join two abutting fills do, which showed on a plain
rounded border where each arc met its straight run.

That also closed the long-standing `double` + `border-radius` fallback. The reason it existed was
structural, not fundamental: a square border paints its bands as *fills*, so N bands is N fills, but a
rounded one paints as a *stroke*, and a pen draws exactly one band - so `double` had nowhere to put
its second line and `GetPen`'s catch-all arm silently degraded it to a single solid stroke. Once the
outline builder took an arbitrary inset (with radii reduced to match), `double` became two calls to
it, at the thirds. `groove`/`ridge` stay on the fallback for a real reason: they shade each side
differently, and one continuous stroke cannot change color partway round.

One ordering detail worth keeping: the outer line is stroked first. It sits closest to the edge and is
therefore the last to run out of room, so bailing on it means nothing has been drawn and the caller can
still fall back. Bailing *after* drawing it would send the caller down the per-edge path and paint the
whole border a second time over what was already there. A closed run has as many gaps as dashes (not n-1), so
`StyledStrokeFitting.FitClosed` is its own function; the perimeter needs an ellipse arc length, which
has no closed form, so `EllipsePerimeter` uses Ramanujan's approximation — accurate to far better than
a dot's width at any radius a border produces.

## Deliberately not done

`text-decoration-style: dotted` still paints square dots on an unfitted period - it goes through the
same `RDashStyle` mapping but the geometry is per text run, not per box edge, and skip-ink
segmentation makes "the edge length" a different question. Left for its own change.

A rounded border whose four sides differ in width or color still strokes each edge's own arc with an
unfitted period, so its marks can still bunch near a corner. Only the uniform case - which is what a
`border-radius` is nearly always paired with - gets the single fitted outline.

## Evidence

Full suite green (11860 passed, net8.0). 100% diff coverage on every changed/added file. Whole
solution rebuilds with zero warnings. The showcase gained rows for per-width scaling, per-side
differences, the border-triangle mitre and rounded corners — each compared against Chrome's render of
the same HTML — and was checked through PDFium *and* MuPDF, which agree (mean channel difference
under 1.4, i.e. antialiasing).
