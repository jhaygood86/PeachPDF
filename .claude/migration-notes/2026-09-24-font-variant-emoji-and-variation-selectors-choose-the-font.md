# `font-variant-emoji` is supported, and U+FE0E/U+FE0F now choose between a text and an emoji font

**Before:** `font-variant-emoji` was not registered, so a declaration was dropped (and `font-variant: emoji`
made the whole shorthand invalid). U+FE0E and U+FE0F were only treated as default-ignorable: they drew
nothing but had no say in which font drew the character before them, so `❤️` (U+2764 U+FE0F) came out as
whichever `font-family` entry covered U+2764 first - the plain outline heart if a text font preceded the
colour emoji font in the list.

**Now:** `font-variant-emoji: normal | text | emoji | unicode` parses and cascades (inherited, reset by the
`font` shorthand, settable through `font-variant`). For a character that has both a text and an emoji form
(the bases in Unicode's `emoji-variation-sequences.txt`) the requested presentation now picks the font: a
`font-family` font that supports the form wins, then system fallback is searched, and only if nothing
supports it is the form ignored. An explicit U+FE0E/U+FE0F in the text overrides the property. `normal`
(the initial value) still makes no choice, so a document that sets neither the property nor a selector
renders as before.

**What a document author could notice, beyond the new property working:**

- A `❤️` (with U+FE0F) whose `font-family` lists a text font before a colour emoji font now draws the colour
  heart, and a `❤︎` (with U+FE0E) whose colour font comes first now draws the outline heart. Text that
  contains a variation selector but relied on the old first-font-wins behaviour changes appearance.
- A font's `cmap` format-14 (Unicode Variation Sequences) table is now read. A variation selector after a
  character the font gives a dedicated glyph for draws that glyph (e.g. STIX Two Math's slashed zero for
  `0` + U+FE00) - previously the selector was dropped and the ordinary glyph always drawn.
