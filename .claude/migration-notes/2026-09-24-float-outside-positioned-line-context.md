# Floats no longer displace content inside independent positioned boxes

**Before:** a wide float preceding an absolutely positioned box could shift that box's
internal inline content beyond the box and even beyond the page. An SVG cart background on
the shifted link could appear missing while an absolutely positioned badge remained visible.

**Now:** the float remains in the surrounding flow, while inline content inside the
independent box is laid out from that box's own content area. The cart background remains
visible at the intended position.

**Why:** the line layout no longer consults floats outside its independent formatting context.
The old `LeftFloatAt` behavior is present in the latest prior tag,
`v0.9.19` (confirmed with `git show v0.9.19:src/PeachPDF/Html/Core/Dom/CssLayoutEngine.cs`).

**What an author may need to change:** nothing.
