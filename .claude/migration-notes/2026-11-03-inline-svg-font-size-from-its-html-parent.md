# Inline SVG: the font size of the HTML parent, and `rem` of the HTML root

For an inline `<svg>` (not a standalone `.svg` file):

- Text, and a relative `font-size` (`2em`, `150%`), that no element in the svg gives a size now start from the font size of the HTML element around the `<svg>`, where they started from the library's fixed 11 (so a `<div style="font-size: 12px">` with an svg whose text declares no size draws it at 12px, not 11px; with no styling at all it follows the page's default size).
- A geometry length in `em` in an svg that declares no `font-size` (`<circle r="2em">`) is of that same size, where it was of 16px.
- `rem` anywhere in an inline svg is the font size of the HTML root element, where it was the size of the `<svg>` element itself (`svg { font-size: 10px }` with `html { font-size: 16px }`: `2rem` is 32 now, was 20).
- The same bases hold for an animation of `font-size` below an `em` or `rem` ancestor and for a keyframe that leaves it out.

What to look for in an existing document: an svg that declares no size on its text now draws it about a third larger on an unstyled page (14.67px, the page default, where it was 11px), so a fixed-size label or chart can clip or overlap; an svg under a `font-size: 0` ancestor (the whitespace-gap trick) loses its text and its `em` geometry, as in a browser; an svg in a heading or a large paragraph follows it. Only the size is inherited: `font-family`, weight and style still are not.

A keyframe length that is a `calc()` or `min()` that comes out negative (`r: calc(-5px)`) is zero where it is used, as in a browser, where it drew a circle of radius 5 at the first keyframe (and the same for `width`, `height`, `stroke-width` and the other lengths that take none).

A standalone SVG document is unchanged. An svg that declares its own size (`<svg font-size="12">`, `svg { font-size: 10px }`) keeps it, and `em` on the `<svg>` itself is of the HTML parent.
