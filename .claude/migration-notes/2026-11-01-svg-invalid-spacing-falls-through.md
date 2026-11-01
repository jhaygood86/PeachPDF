# SVG letter-spacing/word-spacing: an invalid value no longer wins the cascade

Before: an SVG `<text>` with `style="letter-spacing: foo"` and a `letter-spacing="3px"` attribute computed 0 (the invalid style declaration won and read as inherited/initial); a `letter-spacing="calc(1em+2px)"` (no whitespace around `+`) or `"5 px"` read as a valid 0.

Now: an invalid declaration is dropped (CSS Cascade 4), so the presentation attribute answers; a malformed `calc()` or a number separated from its unit computes to the inherited spacing, as in a browser.
