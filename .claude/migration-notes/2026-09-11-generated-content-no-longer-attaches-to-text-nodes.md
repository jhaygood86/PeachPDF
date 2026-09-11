# Generated content no longer attaches to bare text

_Landed 2026-09-11._

**Before:** a universal pseudo-element rule — `*::before`, `*::after`, or any selector ending in one
whose compound reached everything, such as `.wrapper *::before` — generated a pseudo-element box on
**every** box in the subtree, including the anonymous box a bare text node becomes. A text box that
acquired one then held both its own words and child boxes, a shape the inline flow does not flow, and
the text stopped being emitted into the PDF at all: it laid out at the right size, its background and
border painted, and its glyphs were simply absent.

**Now:** a pseudo-element is generated on an element, per Selectors 4 §3.3. A universal rule reaches
every element in the subtree and nothing else.

**What a document author sees:** text inside a document using a blanket `*::before`/`*::after` rule
renders. The idiom is common — `*, *::before, *::after { box-sizing: border-box }` is in most CSS
resets, and Charts.css ships its own copy — so a document could lose an arbitrary amount of its text
with no error and no obvious cause. Documents that were unaffected are unchanged: no pseudo-element
that was being generated on a real element stops being generated, and no styling on one changes.
