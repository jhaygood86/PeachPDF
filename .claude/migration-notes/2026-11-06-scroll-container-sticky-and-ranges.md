# Sticky boxes in scroll containers stick; right-to-left containers scroll to the left

On a screen, a `position: sticky` box inside a scroll container now sticks to the container's scrollport instead of staying where layout put it. A right-to-left (or `vertical-rl`) scroll container now
starts at its right edge and can be scrolled to the content that runs on to the left, where that content used to be unreachable; `ScrollMetrics` reports `MinX`/`MinY` for it. A transformed box inside a
scroll container now lengthens the scrollable area where the transform puts it. A class or attribute change that reloads the document keeps the scroll offsets.
