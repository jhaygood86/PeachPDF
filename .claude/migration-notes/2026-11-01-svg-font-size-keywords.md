# SVG font-size keywords are resolved

Before: on an SVG element, `font-size: initial`, `medium` and the other absolute-size keywords (`xx-small` to `xxx-large`), `smaller` and `larger` all kept the parent's size.

Now: `initial` and `medium` are 16px, `xx-small`/`x-small`/`small`/`large`/`x-large`/`xx-large`/`xxx-large` are 9/10/13/18/24/32/48px, `smaller` and `larger` step the parent's size by 1.2, and `unset` is `inherit`, in the attribute, `style` and stylesheet forms (`xxx-large` is not accepted in a stylesheet rule, as in HTML). An unparseable value still computes to the inherited size.
