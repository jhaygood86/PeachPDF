# A `<br>` no longer indents or blanks the line it opens

Three related changes to how the line after a forced break is laid out. All of them make a `<br>`
behave the way Chromium does.

## Source white space after a `<br>` no longer indents the line

**Before (v0.9.18 and earlier):** collapsible white space that began a line was laid out as a leading
space. Because source formatting collapses to a space, a `<br>` written on its own source line
indented everything after it by one space (3.013pt at the default 12pt font) relative to the line
above:

```html
<div style="display:inline-block; width:450px;">Field name:</div><div style="display:inline-block;">short</div>
<br/>
<div style="display:inline-block; width:450px;">Long field name:</div><div style="display:inline-block;">long</div>
```

The second row's label started 3.013pt right of the first row's, and its value 3.013pt right of the
first row's value. Minifying the same markup so no source newline followed the `<br>` rendered
correctly, so a purely cosmetic change to the source changed the output. The same happened when the
space sat inside the following inline instead (`<br><span> B</span>`).

**Now:** the space is removed, per
[css-text-3 phase II](https://www.w3.org/TR/css-text-3/#white-space-phase-2), which removes a
sequence of collapsible spaces at the beginning of a line. Both forms lay out identically, and match
Chromium exactly: with the UA's 8px body margin and a 450px label, every value begins at `x = 458px`.

## A justified line opened by a `<br>` no longer starts indented

**Before:** `text-align: justify` treated the forced break itself as a justification opportunity, so
it spent one expansion share before the first word of the line the `<br>` opened. At `width: 300pt`,
`first line here<br>alpha beta …` started that line 3.453pt in while the lines around it were flush.
This needed **no white space in the source at all**.

**Now:** a forced break is a line terminator, not a word separator, so the line begins flush. The
share it used to absorb is redistributed over the line's real opportunities, which widen very
slightly.

## An overlong word after a `<br>` no longer blanks that line

**Before:** a word too long for the measure wrapped off the line the `<br>` had just opened, leaving
that line blank and pushing the word a whole line-height further down than the identical content
separated by a space. With `overflow-wrap: break-word` or `anywhere` the emergency split was blocked
outright and the line was wasted entirely.

**Now:** a forced break is not a soft wrap opportunity — [css-text-3 §5](https://www.w3.org/TR/css-text-3/#line-breaking)
does not permit one at the start of a line — so the word uses the line the break opened, and an
emergency split begins there.

## Unchanged, deliberately

- A collapsible space **between** content already on the line is a real word separator and still
  renders — `<span>AA</span> <span>BB</span>` keeps its gap, including after an atomic inline-level
  box (`inline-block`, `inline-table`, `inline-grid`, `inline-flex`).
- Preserved white space (`white-space: pre` / `pre-wrap`) is content, not a collapsible space, and is
  not removed at a line start.
- A soft wrap was already unaffected: the space that permitted the wrap stays on the line it ended.

## What a document author may notice

Content following a `<br>` moves left by one space and now aligns with the line above it; a document
that relied on that space for indentation should use `text-indent`, `margin`, `padding` or `&nbsp;`.
A block containing a `<br>` followed by a word too long for its measure becomes one line shorter, so
surrounding content moves up and page breaks may fall differently. Justified paragraphs broken by
`<br>` have slightly wider word gaps on the lines each break opens.
