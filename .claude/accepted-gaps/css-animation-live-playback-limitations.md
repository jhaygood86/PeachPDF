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
- **Retained layers are drawn in place, not on a layer above the page.** A box animating `transform` or `opacity` is recorded once and drawn again each frame, but the rectangle
  it covers (old and new place) is still repainted from the page behind it, so the cost per frame follows what is under the box (text, blurred `box-shadow`s). On a page of many
  shadowed cards that is a few milliseconds a frame (the animation bench's keyframes showcase). Painting the animated boxes on a surface above the page would not repaint the page
  at all, but it puts them over everything that should cover them, which needs overlap testing the way browsers do before it can be turned on.
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
  a dirty region, not the changed element. A standalone SVG (`<img src="x.svg">`) is not played, and the box of the `<svg>` is laid out once: animating the root's `width`, `height` or `viewBox`
  changes what is drawn inside, not the size of the box.
- **Only the Win2D backend keeps layers as textures and decodes animated images for the WinUI view**; `RasterRenderContext` decodes animated images and the default retained layer works
  on every canvas, but Skia, CoreGraphics and the PDF/SVG writers show the first frame of an animated image.
- **The Android view does not play animations.** Only the WinUI view steps the document each frame (`AdvanceAnimations` on the display's frame clock) and raises the animation events; `PeachBrackets.Android.HtmlView` has no frame loop (a `Choreographer` callback calling `AdvanceAnimations`), so a document shown there is a still frame.
- **Left from the review of the clock** (each is small and has no tracking issue yet):
  - The control API (`DocumentAnimation.Play`, `Seek`, `Finish`, `Cancel`, `Reverse`) wakes the clock but not the WinUI frame loop: once the loop has stopped, the host (or the next scroll or paint) has to call for a frame before the change shows.
  - CSS animations on the elements of an inline `<svg>` ignore `animation-play-state: paused`, and a SMIL `set`/`animate` of the `transform` attribute is ignored (only `animateTransform` and `animateMotion` give a transform).
  - A transition with a `0s` duration and a positive delay snaps at once instead of after the delay, and a layout-property transition that starts before the first step with a canvas is not played.
  - `visibility: collapse` on a table row is applied without a layout pass, so the row keeps its height.
  - A second box of one element (a split inline) can take a spurious `@starting-style` transition, and `HtmlDocument.StartingStyles()` does not restore the styles if the recascade throws.
