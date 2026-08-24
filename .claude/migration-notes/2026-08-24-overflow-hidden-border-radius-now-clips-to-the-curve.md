# `overflow: hidden` + `border-radius` now clips to the curve, and radii reduce jointly

Two related fixes to rounded-corner rendering.

**Overflow clipping.** Previously, an `overflow: hidden` box with a `border-radius` clipped its
descendant content to its padding-edge *rectangle* only — the rounded corners had no clipping
effect on children, even though the box's own background and border painted rounded correctly. A
child that filled the box (a progress-bar fill inside a pill-shaped track, say) showed square
corners poking past where the rounded background implied they should be clipped. Descendant content
is now additionally clipped to the rounded curve itself, per
[CSS Backgrounds and Borders Module Level 3 §5.5](https://www.w3.org/TR/css-backgrounds-3/#corner-clipping).

**Overconstrained radii.** Previously, when a `border-radius` was large enough to overlap on both
axes of a box (most visibly on a short, wide box with a large radius — e.g. a pill-shaped
`border-radius: 999px` progress bar), the horizontal and vertical components of the radius were
reduced by two independent factors, one per axis. This could stretch what should have been a
circular corner into a near-degenerate ellipse with a pointed cap, since the horizontal radius
would clamp only to half the box's width while the vertical radius clamped separately to half its
height. Radii now reduce by a single joint factor applied to both axes together, as the CSS
Backgrounds and Borders specification's corner-overlap algorithm requires, producing a true
circular (or evenly elliptical) corner in every overconstrained case.
