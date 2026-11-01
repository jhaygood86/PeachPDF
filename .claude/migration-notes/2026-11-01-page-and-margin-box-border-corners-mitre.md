# A page or page-margin box's border now mitres at its corners

Before: the four edges of a page box's or page-margin box's border were painted as full-width and full-height strips, so each corner square was painted twice and the left or right edge's colour (or bevel face) covered it entirely.

Now: the border goes through the same routine as an element's, so each corner is split along the diagonal between the two edges meeting there (CSS Backgrounds 3 section 5.2). `border: 20px inset` now shows a proper bevel; `border-top: red; border-right: blue` splits the top-right corner between them.
