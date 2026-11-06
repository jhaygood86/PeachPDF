# Scroll properties now do something on a screen

On a screen, `scroll-behavior`, `scroll-snap-*`, `scroll-margin`/`scroll-padding` (all sides), `overscroll-behavior`, `scrollbar-width` and `scrollbar-color` were parsed as unknown (ignored) before;
now they apply to scroll containers. A document that wrote them for a browser sees a snapped, gliding or thin-barred container in a `HtmlDocument` host. Print and PDF output are unchanged.
