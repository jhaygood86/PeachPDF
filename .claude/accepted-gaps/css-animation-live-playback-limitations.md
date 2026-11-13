# Live CSS animation playback: what is deliberately not modelled

`HtmlDocumentOptions.AnimationsEnabled` plus `HtmlDocument.AdvanceAnimations` play animations, transitions and animated images over time for a screen host (the
design is in [the recent fix](../recent-fixes/2026-11-05-live-animation-clock-and-retained-layers.md)). The still frame a PDF takes
([its own limitations](css-animation-snapshot-limitations.md)) is a separate path and unchanged. These are left out on purpose.

- **A layout animation costs a layout of the whole document per frame.** `AdvanceAnimations(now)` writes only the paint-only set `InteractionRestyler` restyles in place (`opacity`,
  `transform`, `filter`, `mix-blend-mode`, colours, `box-shadow`, `text-shadow`, outlines, ...). `AdvanceAnimations(now, canvas)` also plays `width`, `margin`, `top`, `font-size` and the
  like, by writing the value and laying the document out again; the engine has no layout of just the animated box's subtree (nothing marks what is dirty, and a block's bottom
  only ever grows in the resumed layouts it has), so the cost follows the document's size, not the box's. It plays on a `MediaType.Screen` document without container queries
  only (a container query re-cascades the document through a new box tree; a paged one has no single surface); on any other the properties stay where the cascade left them.
  A registered custom property (`@property`, with a syntax that mixes) plays the same way, and costs the same: the box and everything under it is cascaded again each frame, since
  any of it may read the value through `var()`. Not played at all: the properties that change what boxes there are (`position`, `float`, `content`, ...), `display` (it only
  transitions, with `allow-discrete`, as a step that keeps the box for the whole interval), and an unregistered custom property or one whose syntax does not mix (a step).
- **A `transform`, `filter` or `perspective` that appears on a box with positioned descendants** makes the box their containing block, which moves them; the in-place restyle
  refuses that change and reloads the document, the live clock does not and the descendants stay where they were laid out.
- **A promoted box is drawn apart from the page only when the whole group can be layered.** `DocumentPaintLayer.Animated` (see [the recent fix](../recent-fixes/2026-11-06-animated-layer-and-android-animation.md) and
  [the one that widened it](../recent-fixes/2026-11-12-animated-layer-ancestors-and-overlap.md)) takes a box animating `transform` or `opacity`, with what has to be drawn with it, out of the surface a host keeps.
  A box under clipping ancestors is drawn under their clips; one inside a stacking context (an `opacity` group, a filter, a transform) is drawn by painting that whole group; a positioned box painted
  over it joins it. What still stays in the content layer and repaints the rectangle it covers: a group that blends (`mix-blend-mode`) or reads its backdrop (`backdrop-filter`), one that holds
  a fixed or sticky box, a box a fixed or sticky box (which the viewport layer always draws on top) is meant to sit under, in-flow content that paints over a box with a negative `z-index`, and a
  set that grew past a cap (twelve boxes, or a group over 640000 square points that is more than eight times the animated boxes in it). Outlines and collapsed table borders of an ancestor paint after its
  positioned children, so a box that moves under one of those edges is drawn over it. The placements, links and text runs a paint records are written for a box when the animated layer draws it.
  The test is made at each step, so a box that crosses another one between two steps is placed by where it is at each.
- **A retained layer is recorded at the density it was first drawn at.** A box that scales up a lot is resampled, not re-recorded.
- **A `background-image` GIF or WebP shows its first frame**; only `<img>` plays. (The image cache keys by address, not by the box that shows it.)
- **A transition starts from every restyle that loads the document again** (the host's class, attribute and preference changes, a state rule that moves something) by comparing each
  element's style before and after, and from `@starting-style` when the document first appears or an element is shown again. The ones it does not run are an inherited property that
  moves boxes, such as `font-size` (the boxes under it would have to be cascaded again at the start), a property a running animation animates, a layout property (or `display`) when
  the host does not step the document with a canvas, and anything on a pseudo-element (`::before`, `::after`, `::marker` have no source ordinal to follow across the load). The
  `@starting-style` of an element that is new gets the style of the element's own rules only: what it inherits is the parent's ordinary style, not the parent's starting style. A
  change applied in place, as a state rule that only paints is, is not compared: it starts its transitions from the properties it changed. A transition of an inherited property that
  only paints (`color`, `visibility`) shows the new value on the boxes under the one that transitions for the first frame, until the clock cascades it down. `visibility: collapse`
  on a table row does not lay the table out again while it transitions.
- **Scroll-driven timelines and `animation-range`** are not supported, as in the still frame.
- **SVG animation is played, but the graphic is rebuilt for each frame.** CSS animations of the elements of an inline `<svg>` and its SMIL animations (see [the recent fix](../recent-fixes/2026-11-05-svg-animation-playback.md))
  rebuild the whole scene graph at the clock's time and repaint the `<svg>`'s rectangle; a graphic with thousands of elements animating costs a rebuild of all of them. Only the `<svg>` as a whole is
  a dirty region, not the changed element. The box of the `<svg>` (and of an `<img>` of a standalone one) is laid out once: animating the root's `width`, `height` or `viewBox` changes what is drawn inside, not the size of the box.
  A standalone SVG is played when an `<img>` or `<object>` shows it, not when it is a CSS `background-image`, and a CSS animation in it that needs `var()` is left out. The elements of an
  inline `<svg>` are not restyled while it plays (they are detached from the cascade), so a `:hover` rule on one of them, or a change to its `animation-play-state`, does not take effect.
  **The repaint is the whole rectangle of the `<svg>` or image, not the changed element:** a bounding box that is safe to repaint needs the painted extent of every kind of element (stroke, miter, markers, filters, masks, clips, `<use>`, text, transforms),
  and one that is too small leaves stale pixels, so it was not attempted; a frame costs about a millisecond on the animated SVG showcase.
- **Only the Win2D backend decodes animated images for the WinUI view**; `RasterRenderContext` decodes animated images and the default retained layer works
  on every canvas, but Skia, CoreGraphics and the PDF/SVG writers show the first frame of an animated image. Win2D and Skia keep a retained layer as a texture (Skia records it on the CPU
  and uploads it once; drawing it into a GPU render target mid-frame blanked the frame on the Android emulator, so it does not).
- **The Android view's text positions are not coarsened while it plays**, as the WinUI view's are (`Win2dRenderContext.TextPositionSteps` has no Skia counterpart: Skia has no text mask cache to keep small).
- **Left from the review of the clock** (each is small and has no tracking issue yet):
