# Default margins and heading sizes match the browsers'

Before: the built-in stylesheet used CSS 2.1's sample values: `1.12em` vertical margins on `p`, `ul`, `ol`, `dl`, `blockquote`, `menu`, `dir`, `form` and `fieldset`; `h2 .75em`, `h3 .83em`, `h4 1.12em`, `h5 1.5em`, `h6 1.67em` margins; `h6` at .75em.
Now: the HTML Standard's rendering-section values every browser uses: `1em` on `p`, lists, `blockquote`, `menu`, `dir`; headings `.67em`, `.83em`, `1em`, `1.33em`, `1.67em`, `2.33em`; `h6` at `.67em`; `form` has no margin and `fieldset` a 2px inline margin. A document with no stylesheet of its own has slightly tighter paragraph and list spacing (16px, not 17.9px, at 16px type) and different heading spacing; set the margins explicitly to keep the old numbers.

# `margin` and `padding` no longer apply to internal table boxes

`display: table-row-group | table-header-group | table-footer-group | table-row | table-column-group | table-column` ignore `margin` and `padding`, and `table-cell` ignores `margin` (CSS 2.1 §8.3/§8.4); both used to apply, moving rows and groups.
