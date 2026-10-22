# Under `white-space: nowrap` or `pre`, a float wider than its column covers the text after it (#1891)

**Gap:** in a multi-column container with `white-space: nowrap` (or `pre`), a float wider than the column that follows a `<br>` is drawn over the text on the next line. The words are in the PDF and are positioned as on `main` with an explicit `<div>` around the text, but they sit under the float and cannot be seen. A browser moves that line below the float.

**Why:** `ShiftEmptyLineBelowCrowdingFloats` is not run for `nowrap`/`pre`. Running it for them moves the line below the float, but the line then lies past the column's band, is deferred to the next fragmentainer, and the line before it is drawn twice (measured on `columns:3; width:650px`, a 280px right float after `<br>`: `w00005` twice and the rest on page 2). A fix has to stop the deferral from re-emitting the previous line first.

**Not claimed:** that `main` (no wrapper, so no columns) overlaps: it draws the words beside the float.

**Any float after a `nowrap`/`pre` line:** the same holds without a `<br>`. A float that follows text on such a line is placed at the line's top and left over the words, as `main` with an explicit `<div>` does; the line is not moved beside the float or broken after it, which drew words twice (see the [recent fix](../recent-fixes/2026-10-20-a-float-after-text-moves-the-text-beside-it.md)). A browser keeps the line whole too, so the gap is the same: a float wider than what is left of the column covers the words after it.
