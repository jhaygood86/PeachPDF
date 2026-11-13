# An absolute box with no positioned ancestor is placed from the page area's corner; the root element contains its margins and floats

Before: such a box was placed from the `html` box, whose top included the margin the body and its first child collapsed through it, so `top: 0` sat
at the body's margin (8px by default) down, not at the page area's top. A first child with a top margin taller than a page paginated as blank pages.
Now: it is placed from the corner of the page area (CSS 2.1 section 10.1); the `html` element establishes a block formatting context, so its
margins do not collapse and it contains its floats; an oversized first-child top margin is truncated at the first unforced page break like any other.
A document that used `position: absolute` with `top`/`left` and no positioned ancestor moves up by its body margin (and by anything collapsed into
the top of the root).
