# SVG text: `inline-size` and `shape-inside` now wrap

_Landed 2026-10-03._

Before: `inline-size`, `shape-inside`, `shape-subtract`, `text-indent`, `line-height`, `hyphens` and `text-align` had no effect on SVG `<text>`; a `<text>`
was always one line, a line feed in preserved text (`white-space: pre`, `pre-wrap`, `pre-line`) was a space, and `white-space: pre-line` collapsed everything
as `normal` does. Checked against the previous tag: `docs/supported-svg-features.md` listed all of these as not applied.

Now: a `<text>` with an `inline-size` (or a `shape-inside`) is laid out in auto-wrapped line boxes. Its lines break at Unicode line breaking opportunities and
at soft hyphens/hyphenation points, a line feed under `pre`/`pre-wrap`/`pre-line` is a hard break, `line-height` sets the line spacing, `text-indent` indents the
first line, and `text-align` (or, without it, `text-anchor`) aligns each line, including `justify`. Text with no `inline-size`/`shape-inside` is unchanged.

Visible consequences for existing documents: a `<text>` that already carried one of these properties together with `inline-size` or `shape-inside` (previously
ignored, so it drew as a single line) now wraps; `textLength` and per-character `x`/`y`/`dx`/`dy` on such a `<text>` are ignored, where before they applied to the
single line. Nothing changes for text that sets neither `inline-size` nor `shape-inside`.
