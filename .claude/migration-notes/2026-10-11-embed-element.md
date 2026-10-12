# `<embed>` is a void replaced element

**Before:** `<embed>` was an ordinary inline box that was not void, so the content after it was nested inside it, nothing was drawn for its `src`, and `width`/`height` sized nothing (a `background` landed on the following text). `<wbr>` was not void either.

**Now:** `<embed>` is void and an atomic inline replaced box. An image or SVG `src` is drawn at the `width`/`height` size (with `object-fit`/`transform` applying); any other resource reserves an empty box of that size. `<wbr>` no longer swallows the content after it.

**Why:** HTML defines both as void elements; browsers treat `<embed>` as replaced (issue 1707).
