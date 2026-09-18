# inline-table, inline-grid, and inline-flex now wrap onto a new line when they don't fit

CSS 2.1 [§9.4.2](https://www.w3.org/TR/CSS21/visuren.html#inline-formatting) makes an atomic
inline-level box (one that establishes its own formatting context - `inline-block`, `inline-table`,
`inline-grid`, `inline-flex`) one unbreakable unit on the line it sits on: when it does not fit in
what is left of the line, the line closes and the box starts the next one, the same way a word wraps.

`inline-block` already did this. `inline-table` and `inline-grid` reached the same placement code as
`inline-block` but skipped the fit check that decides whether to wrap; `inline-flex` had no fit check
of any kind. All three previously stayed on the current line unconditionally, however far their
margin box overflowed the containing block's right edge - a fixed-width row of `inline-table`/
`inline-grid`/`inline-flex` cards ran off the page instead of wrapping the way a browser does.

All three now preflight an estimated used width - a declared, non-percentage `width` as-is, or
otherwise their own max-content width bounded by the containing block (CSS Flexbox 1 §9.2's
shrink-to-fit main size for `inline-flex`) - against the remaining line measure, and move to a new
line when it does not fit. Each engine still settles its own real used width independently once its
own layout (column/track algorithm, or the flex algorithm) actually runs; the preflight estimate is
discarded either way.

Confirmed against `git show v0.9.18:docs/html-css-support.md`: at the last release, no atomic
inline-level box moved onto a line of its own at all (the doc's atomic-inline-level-layout section
described only the unrelated `inline-block` declared-`height` gap) - `inline-block` gained its own fit
check and wrap after that release, and this closes the same gap for `inline-table`/`inline-grid`/
`inline-flex`, which is a genuine behavior change relative to v0.9.18, not a pre-existing difference.
