# `text-transform: uppercase`/`lowercase` map whole code points

`TextTransformer.Apply` mapped a UTF-16 unit at a time with `char.ToUpperInvariant`, so a letter outside the BMP (Deseret, Osage, Adlam) was two lone surrogates that map to themselves, and the dotless i (U+0131), which .NET's invariant mapping leaves alone, stayed `ı` where Unicode's simple mapping gives `I`. `MapCase` now walks `Rune`s and special-cases U+0131; the length stays the same, which the box's word offsets rely on.

**Measured:** css-text +3 on a Mac (`text-transform-upperlower-005/028/029`), nothing lost (floor 825 -> 828).

**Not done, found while measuring:**
- *Full case mapping* (ß -> SS, ŉ, ǰ, ΐ, the Latin and Armenian ligatures, Greek with iota subscript: 102 characters uppercase to more than one) and the language tailorings (Turkish/Azeri i, Lithuanian) are what most of the remaining `text-transform-upperlower-*` tests want. They change the text's length, and `CssBox.AppendWordsFromText` indexes `BidiLevels`, the break opportunities and the original-text slice by the position in the transformed text, so that needs reworking first (a length-changing transform would read past them).
- `text-transform: full-width` maps a space to U+3000 *before* white space collapses, so `x<span>   </span>x` becomes three ideographic spaces instead of one (css-text-3 collapses first). It was hidden while U+3000 collapsed, and is what `text-transform-fullwidth-006/008` now show.
