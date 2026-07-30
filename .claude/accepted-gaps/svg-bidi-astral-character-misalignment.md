# SVG bidi resolution indexes UTF-16 code units against per-Rune glyph ordinals

Tracking issue: [#555](https://github.com/jhaygood86/PeachPDF/issues/555).

`SvgRenderer.RenderText`'s `ApplyBidiReordering` builds `paragraphText` by concatenating each
`GlyphInfo.Glyph` into a UTF-16 `string` and passes it to `BidiResolver.Resolve`, then calls
`BidiResolver.ReorderLine(result.Levels, 0, glyphs.Count)`, treating `Levels`' indices as glyph
ordinals. But `FlattenRun` emits exactly one `GlyphInfo` per `System.Text.Rune` (one glyph per full
codepoint), while `Resolve` returns one level per UTF-16 code unit (a surrogate pair is two code
units for one Rune/glyph).

Any codepoint above U+FFFF (an astral character — a rare script, or an emoji) makes
`paragraphText.Length` exceed `glyphs.Count`; every glyph after it is assigned the level meant for an
earlier character, and `BidiIsolateOverride` ranges (built from glyph ordinals) end up covering the
wrong span too. The HTML path avoids this by keying levels to UTF-16 string indices consistently
throughout, never re-indexing into a separately-counted glyph list.

**Deliberately out of scope for now.** Low real-world frequency (requires an astral character
specifically inside bidi-resolved SVG text), and the fix needs `FlattenRun`/`ApplyBidiReordering` to
carry UTF-16-length metadata per glyph rather than assuming a 1:1 glyph-to-code-unit correspondence -
a real, if narrow, change left for a follow-up.
