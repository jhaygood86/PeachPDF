# An underlined vertical-writing-mode inline-block is not underlined across every column

Under `writing-mode: vertical-rl`/`vertical-lr`, an `inline-block` with `text-decoration` now takes the
propagated-decoration path (it used to draw a full-height bar down its edge, empty or not). Rendering
`display:inline-block; writing-mode:vertical-rl; text-decoration:underline` with wrapped text
(`abc def`) showed a line along only one column of the text, not each column. Observed by rasterizing; the
cause was not traced - vertical layout is itself an accepted gap, see
[no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md), and may be what orders or
groups the columns' line boxes.

Better than the full-height bar and no worse than the neighbouring vertical decoration gaps in
[text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md](text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md).
Documented in the `text-decoration-line` row of `docs/html-css-support.md`. Tracking issue: [#1617](https://github.com/jhaygood86/PeachPDF/issues/1617).
