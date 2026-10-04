# An underlined vertical-writing-mode inline-block is not underlined down every column

Under `writing-mode: vertical-rl`/`vertical-lr`, an `inline-block` with `text-decoration` takes the
propagated-decoration path. Before, it drew a full-height bar down its edge, empty or not; now an empty
one has no line, and one with wrapped text (`abc def`) gets a short line at the box's left edge rather
than an underline down each text column.

**Verified:** the rendered result above (rasterized, and measured by the PR reviewer as a short stub at
the box's left edge).

**Not verified:** why. Vertical layout is itself an accepted gap, see
[no-vertical-writing-mode-layout.md](no-vertical-writing-mode-layout.md); it may be what orders or groups
the columns' line boxes, but that was not traced.

No worse than the neighbouring vertical decoration gaps in
[text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md](text-decoration-skip-ink-and-atomic-inline-exclusion-are-horizontal-only.md).
Documented in the `text-decoration-line` row of `docs/html-css-support.md`. Tracking issue:
[#1617](https://github.com/jhaygood86/PeachPDF/issues/1617).
