# A gradient with a translucent stop is no longer dimmed by an earlier fill's alpha

A gradient fill whose stops are translucent (`stop-opacity`, `rgba()`, `transparent`) drew too faintly when the fill painted just before it had an alpha (an SVG shape with `fill-opacity`, an HTML element with an `rgba()` text colour or
background colour): that alpha was applied to the gradient a second time. It now draws as its stops say, as a browser does (after `color: transparent` text, a translucent `background-clip: text` gradient used to disappear and now draws) (measured against Chrome: a translucent `linear-gradient()` background after `color: rgba(...)` text was
about a third too faint before, and matches now; radial and conic gradients, and a gradient shape with its own `fill-opacity` that follows an alpha shape change the same way). A document with a translucent gradient
that sits after a semi-transparent solid fill gets stronger; an opaque gradient, a gradient painted first, and a gradient stroke are unchanged
(`git show v0.9.23:src/PeachPDF/PDF/Rendering/PdfGraphicsState.cs` has the reset only in the opaque-gradient arm).
