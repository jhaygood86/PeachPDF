# Underlines now skip descenders, and decoration lines break around atomic inlines

Two changes to how a text decoration line is drawn. Both alter the rendering of documents that
declare nothing new, so both are visible without any authoring change.

## An underline or overline is interrupted where it crosses glyph ink

**Before:** every decoration line was one unbroken stroke. An underline ran straight through the
descenders of `g`, `j`, `p`, `q`, `y`.

**Now:** `text-decoration-skip-ink` (css-text-decor-4 §2.5) is implemented, and its initial value
`auto` skips — which is what browsers do, and so what a document author comparing a PDF against a
browser expects. The line is interrupted wherever it would cross a glyph's ink, with a small gap
either side that grows with the decoration thickness, up to a cap.

**Why the default changed rather than being opt-in:** `auto` is defined as user-agent discretion,
so either behaviour is conformant — but an author who writes no declaration at all is asking for
"whatever is normal", and in every browser that is skipping. Making `auto` mean "don't skip" would
have left the property unable to reproduce the common case at all.

**To restore the old rendering,** declare the opt-out. It inherits, so one rule covers a document:

```css
:root { text-decoration-skip-ink: none; }
```

A `line-through` is unaffected — the spec never skips it.

The line breaks once per glyph, over everything that glyph puts in the line's path. A letter the
line meets in more than one place — the two sides of an `o`, the bowl of a `g` — therefore gets a
single gap spanning the whole letter, not one gap per stroke with a stub of line stranded inside it.
css-text-decor-4 [§2.10.5 Shaping Interruptions](https://drafts.csswg.org/css-text-decor-4/#ink-skip-shape)
leaves the shape of the interruption to the user agent, naming "whether to show the line within
enclosed areas of a glyph" as exactly such a choice and warning that following each contour can leave
"typographically-awkward wisps of underline"; Chrome and Firefox both break per glyph, so this is
also what an author proofing against a browser sees.

Two kinds of text still get an unbroken line because no ink can be measured for them: text in a
CFF/OpenType (`.otf`) font, and a run painted by per-codepoint font fallback. See the
`text-decoration-skip-ink` row in `docs/html-css-support.md`.

## A decoration line breaks around an atomic inline

**Before:** a decoration line was drawn straight across any atomic inline on the line — an `<img>`,
an `inline-block`, an `inline-table` — which css-text-decor-3 §2.4 says are not decorated.

**Now:** the line stops at the atomic inline's margin box and resumes after it.

**This was usually invisible, which is why it lasted.** The common atomic inlines paint opaque
content exactly where the wrong line was, so a rasterized page showed the gap the spec asks for and
hid the reason it was there. Documents that will actually look different are the ones where the
atomic inline is transparent, is narrower than its margin box, or carries visible margins — there,
a line that used to run underneath it no longer does.

There is no opt-out: §2.4 states this unconditionally, and it is not tied to a property.
