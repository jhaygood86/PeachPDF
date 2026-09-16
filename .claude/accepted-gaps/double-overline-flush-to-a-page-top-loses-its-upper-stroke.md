# A double overline flush against the top of a page loses its upper stroke

`text-decoration-style: double` grows an overline's pair **upward**, which is what browsers do. That
makes it the one decoration that reaches above the box it belongs to, and with no headroom above the
text the upper stroke lands outside the page and is not drawn.

Measured, Letter page, zero page margin, `body { margin: 0 }`, 16px text: the page clip runs y 0..792
in PDF user space, and the two strokes are emitted at **794.0** and **792.0**. The upper one is 2pt off
the page. The same document with `body { margin: 20pt 0 0 0 }` puts them at 774 and 772, both well
inside.

Not worked around, for two reasons. Moving the pair down so it fits would put an overline somewhere it
was not asked to be, silently, on exactly the documents where the author can see it least. And nothing
is lost relative to a browser: **Chrome 141 given the same markup loses the overline entirely** — both
the double and the single-stroke case — because it too positions it above the content and there is no
page there. PeachPDF still draws the lower stroke, so it is the more forgiving of the two.

The painter emits both strokes either way; it is the page clip that removes one. Pinned by
`TextDecorationDoublePdfClipTests`, which reads the stroke coordinates back out of a real PDF's content
stream against the page clip — `TestRecordingGraphics` records draw calls and never applies clipping, so
no draw-call test in this area can see this at all.
