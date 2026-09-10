# Inline markup no longer creates a line-break opportunity

Previously, adjacent text separated only by an inline element boundary could wrap at that boundary.
For example, `text<span>more</span>` treated the start of the span like a space. With
`overflow-wrap: anywhere`, this could move the entire span to a new line before breaking inside it.

Inline markup is now transparent to line breaking. With no authored break opportunity, adjacent runs
remain one token; `overflow-wrap: anywhere` or `break-word` uses the remaining space on the current
line before continuing on the next.

Emoji assigned ordinary Unicode line-break classes now wrap between complete grapheme clusters,
including when the source places the run inside a span. Variation selectors, skin-tone modifiers, flags,
tag sequences, and ZWJ sequences remain intact. A variation selector no longer contributes an extra
glyph advance in a font that maps it, so emoji such as `✌️` occupy the same line width the browser uses.
An authored space before a following long token retains priority over `overflow-wrap` and starts that
token on a fresh line.
