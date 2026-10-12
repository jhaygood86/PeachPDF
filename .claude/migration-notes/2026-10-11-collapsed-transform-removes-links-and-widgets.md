# Links, form fields and 3D contexts of an element with no visible area are no longer written

**Before:** `<div style="transform: scale(0)"><a href="...">x</a></div>` painted nothing but still wrote an active
`/Link` annotation (and, with interactive forms, widgets) at the untransformed rectangle; a collapsed
`transform-style: preserve-3d` root was still composed into a bitmap (failing `PdfA1B` without flattening);
`scaleZ(0)` / `scale3d(1,1,0)` / a `matrix3d` with a zero z row painted the element although its transformation matrix has
no inverse; and a plane turned edge-on under `perspective` left a hairline bitmap and an invisible text layer.

**Now:** each is treated as the 2D case already was (css-transforms-1 "Transform Rendering"): the element and its
subtree are not painted, a link or form field inside is not emitted, a collapsed 3D context root composes nothing, and an
edge-on plane under perspective (under about 0.017 px thick) emits no bitmap and no text layer. Bookmarks and named
destinations of a collapsed heading are kept. A document that relied on a `scaleZ(0)` element still showing, or on a
link over a `scale(0)` element, needs the transform changed.

The links are also left out of `HtmlContainer.GetLinks()` (as it already leaves out a link whose `visibility` is not
`visible`), but only for a container that has been painted: what is dropped is what the painter did not draw, so a plane
that is edge-on only as a flat transform but is drawn in perspective keeps its link and fields.
