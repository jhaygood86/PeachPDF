# Auto horizontal margins re-centre per page

With per-page `@page` left/right margin overrides, a block with a definite width and `margin: 0 auto` (or a single `auto` margin) used to keep the X it got on the containing block's start page. It now centres (or pushes to the end edge) against the content width of the page it lands on, matching Chromium and Prince. Documents with uniform page widths are unaffected.
