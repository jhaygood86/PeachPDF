# `-webkit-text-stroke`, `text-rendering`, `font-language-override` apply, and baselines read the `BASE` table

**Before:** `-webkit-text-stroke` (and `-width`/`-color`), `text-rendering` and `font-language-override` were dropped at parse time, for HTML
and SVG text alike. SVG `dominant-baseline`/`alignment-baseline` were approximated from font metrics even when the font states its own
baselines, only moved horizontal text, and had no effect on a `<textPath>`.

**Now:**

- `-webkit-text-stroke*` strokes the glyph outlines of HTML words (over the fill) and of SVG text (where it replaces `stroke`/`stroke-width`).
  A document that carried the prefixed property and was unstyled by it before now shows an outline.
- `text-rendering: optimizeSpeed` turns off kerning and the optional ligatures where `font-kerning` is `auto` and `font-variant-ligatures` is
  `normal`; the other keywords change nothing.
- `font-language-override: "SRB"` selects that OpenType language system for the font's `GSUB` features, so a font's language-specific
  alternates change the shaped glyphs.
- SVG `ideographic`, `hanging`, `mathematical` and `central` baselines come from the font's `BASE` table when it has one (text in such a
  font moves by the font's value instead of the metric approximation), baseline properties now move a vertical-writing column sideways
  (a column that sets none stays centred), and they move glyphs on a `<textPath>` along the path's normal.
