# Collapsible white space now collapses across an inline element boundary

Previously, collapsible white space only collapsed within one text-owning box's own string
(`CssBox.ParseToWords`/`AppendWordsFromText`). Two separate collapsible-space runs on either side of
an inline element boundary — most visibly a content-less inline between two words, e.g.

```html
<span>Z</span> <span style="padding-left:20pt; border-left:1pt solid red"></span> <span>BB</span>
```

— each rendered its own space, producing a gap one collapsed-space-width too wide. A chain of several
such boundaries (multiple empty inlines in a row, or a whitespace-only inline like `<span> </span>`
adjacent to ordinary source white space) compounded the same way.

Both spaces now collapse to exactly one, per
[css-text-3 §4.1.1 phase I](https://www.w3.org/TR/css-text-3/#white-space-phase-1): "any collapsible
space immediately following another collapsible space — even one outside the boundary of the inline
containing that space, provided both spaces are within the same inline formatting context — is
collapsed to zero advance width." A new DOM-normalization pass
(`DomParser.CollapseWhitespaceAcrossInlineBoundaries`) runs once, before layout, and removes the
second (and any further) run in a chain, in source order.

This does **not** affect:
- White space inside a `white-space: pre`/`pre-wrap` box, on either side of the boundary — neither is
  ever collapsible, so the exemption is unconditional.
- A run separated by an **atomic inline-level box** (an image, `inline-block`, `inline-table`,
  `inline-flex`, `inline-grid`, `iframe`, `math`, or a form field) — that is real content standing
  between the two runs, not "outside the boundary of the inline containing that space," so both
  spaces survive untouched.
- A `<br>` between the two runs — a forced line break is real content for this purpose too; the space
  around it is governed by the existing, separate layout-time phase II (line-start) removal, not by
  this pass.
- White space already collapsed within a single text node, which was already correct and is
  unchanged.
