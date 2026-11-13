# User-agent default margins and heading sizes follow the HTML Standard

`CssDefaults.DefaultStyleSheet` mirrored CSS 2.1's *informative* sample stylesheet (Appendix D), whose `1.12em` margins on `p`, lists and `blockquote`, and `.75em`/.83em`/1.12em`/`1.5em`/`1.67em` heading margins differ from what every browser (and the HTML Standard's rendering section) uses. A `p` was 18px of margin where a browser has 16px, which moved everything below it by 2px in a WPT reftest that computes an absolute offset (`margin-top: 216px` after a `p`) - and there are thousands of tests with a leading "Test passes if..." paragraph.

Now: `p`, `blockquote`, `ul`, `ol`, `dl`, `dir`, `menu` 1em; `h1` .67em; `h2` .83em; `h3` 1em; `h4` 1.33em; `h5` 1.67em at .83em; `h6` 2.33em at .67em (it was .75em); `form` none and `fieldset` 2px inline (browsers' values) where both were 1.12em.

css +175 passes (css2-flow, floats, positioning and others), 6 lost that were `<form>` tests and recovered with the form change; net +180. See the migration note: every document's default spacing changes slightly.
