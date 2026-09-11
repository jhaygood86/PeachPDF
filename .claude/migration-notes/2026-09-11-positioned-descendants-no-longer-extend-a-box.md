# A positioned child no longer stretches its parent's border and background

_Landed 2026-09-11._

**Before:** a box's painted border box grew downwards to cover an absolutely- or fixed-positioned child
that sat below it. The child is out of flow, so layout placed everything correctly — but the parent's
own `border`, `background` and background positioning area were all drawn against the stretched
rectangle, so its bottom border appeared below the positioned child instead of at the box's own edge.

**Now:** only in-flow content extends what a box paints (CSS 2.1 §9.3.1/§10.6.3). An in-flow child that
overflows its parent still does, exactly as before.

**What a document author sees:** the common "hang a label under this box with `position: absolute` and a
negative margin" pattern stops dragging the box's own bottom border down with it. In Charts.css that is
the difference between a chart's primary axis sitting under its bars, where it belongs, and sitting
below the `Q1`–`Q4` labels.

---

# A repeating background tile is placed at its exact size

**Before:** a tiled gradient background was repeated at a whole number of points, because the tile's
natural size was read through an integer pixel count. A tile of 32.65pt repeated every 32pt, and the
error accumulated once per tile rather than staying sub-point.

**Now:** the tile repeats at the size `background-size` resolved.

**What a document author sees:** a repeating background tile whose size is not a whole number of points
— `background-size: 100% calc(100% / 4)` on a box of arbitrary height is the everyday case — lines up
with the box it came from instead of drifting further out of register with every repetition. The same
applies to any tiled vector content: SVG patterns, masks and opacity groups share the mechanism.
