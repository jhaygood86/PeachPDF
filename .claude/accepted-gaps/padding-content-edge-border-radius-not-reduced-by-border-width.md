# Padding/content-edge rounded curves don't subtract border width from the radius

Every place that computes a rounded curve for a box's padding edge or content edge —
`background-clip: padding-box`/`content-box` (`FragmentPainter.Decorations.cs:98,123`) and the
`overflow: hidden` + `border-radius` descendant-clip curve (`RenderUtils.TryPushOverflowClip`,
`FragmentEmitter.OverflowClipOf`) — calls `CssBox.ComputeRadii(rect)` with the box's raw, declared
border-box radii applied directly to the smaller inset rectangle, rather than first reducing each
radius by the border width (and, for the content edge, the padding too), clamped to zero.

Per [CSS Backgrounds and Borders Module Level 3 §5.5](https://www.w3.org/TR/css-backgrounds-3/#corner-clipping):
"The padding edge (inner border) radius is the outer border radius minus the corresponding border
thickness. In the case where this results in a negative value, the inner radius is zero." This is a
genuine spec deviation, not matched by any accepted browser behavior difference.

For a thin border relative to its radius the visual difference is subtle; for a thick border
relative to a smaller radius (e.g. `border: 6px solid; border-radius: 14px`) the padding-edge curve
bulges further into the corner than the border's own inner edge implies, visibly disagreeing with
the stroke that encloses it.

**Deliberately out of scope** of the `overflow: hidden` + `border-radius` clip-curve fix
([issue #812](https://github.com/jhaygood86/PeachPDF/issues/812)): that fix's new clip curve
deliberately reuses `ComputeRadii(paddingRect)` the same way the pre-existing `background-clip:
padding-box`/`content-box` code already does, so the two stay consistent with each other. Fixing
only the new clip curve (and not the older background-clip path) would make a box's own background
and its content-clip curve disagree with each other — worse than the current consistently-wrong
state. A correct fix needs an inner-radius reduction added to (or alongside) `ComputeRadii` and
applied everywhere a padding- or content-edge curve is computed, together, then re-verified visually
(rasterized with both PDFium and MuPDF, per this repo's testing convention) since it changes
rendered output for every existing rounded-border-plus-inset-background-clip case in the suite and
showcases. Tracked as [issue #817](https://github.com/jhaygood86/PeachPDF/issues/817).
