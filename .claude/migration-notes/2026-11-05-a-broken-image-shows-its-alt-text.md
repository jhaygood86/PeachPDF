# An image that cannot be loaded shows its `alt` text

**Before.** A missing or undecodable `<img>` was an empty box of a fixed default size, whatever its `alt` said: a page that links images that are not there (a saved page with
its assets missing, a blocked request) showed blank gaps.

**Now.** If the image has a non-empty `alt`, the box is that text: sized to it (unless `width`, `height` or CSS size the image) and drawn in the image's own font and colour, clipped to
the box, as a browser shows a broken image. An image with no `alt`, or an empty one (a decorative image), is unchanged. The text is part of the page's drawing, so it appears in the PDF.

Not verified against `v0.9.26-preview1`.
