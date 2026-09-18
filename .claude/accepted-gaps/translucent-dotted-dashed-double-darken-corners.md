# Translucent dotted/dashed outlines double-darken the corner squares (issue #1222)

A `dotted` or `dashed` outline whose color carries alpha paints its corner
squares darker than its runs. Each straight edge is stroked half a width past
both of its ends — the corner squares belong to no edge, so both edges reaching
one cover it, which is also what puts a dash on every corner. With an opaque
color that overdraw is invisible; with a translucent color the corners get two
coats of alpha.

Chrome renders the whole outline at uniform translucency: a non-solid
translucent outline is wrapped in an alpha layer (painted opaquely into an
isolated group, then composited once), so the corner overlap never
double-darkens.

Tracked as #1222 rather than prose alone, since it is a systematic visual
deviation from Chrome on every translucent patterned outline.

## Why it was out of scope

This is not a regression from the outline-union work: main's single-box path
paints per-side strokes that ink the same corner pixels from both sides, so an
unbroken box shows the same darker corners. Fixing it means painting the
patterned strokes opaquely into an isolated transparency group and applying
the alpha once — in the union region path
(`OutlineRegionPainter.Styled.StrokeStraightPattern`) and in the single-box
path (`BoxEdgesDrawHandler.DrawGeneralPatternedSide`) together, so the two
never disagree with each other. That is a compositing change across two paint
paths, not the clip-geometry change this work was scoped to.

## Trap for whoever picks it up

Rounded outlines stroking one closed path do not overlap except at the pattern
closure point, and `solid`/`double` fill without overlapping strokes — only the
square-cornered per-edge patterned strokes need the group. And whatever group
is introduced must cover both the union path and the single-box path: fixing
one while the other keeps double-painting just moves the inconsistency
somewhere a reader will trip over it.

Reader-facing note lives in `docs/html-css-support.md` (without the issue number, per the docs rule).
