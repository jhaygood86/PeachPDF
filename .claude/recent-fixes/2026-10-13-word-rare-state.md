# CssRectWord: the hyphenation, overflow-wrap and scaled-font members live in WordRareState

A multi-column document of 1.5 million words kept 400 MB of `CssRectWord`s live (264 bytes each) - a third of a 1.2 GB heap whose
gen2 collections are ~20% of the render. Most of the instance was members a plain Latin word never has: hyphenation pieces,
overflow-wrap pieces, a pre-split link, a first-line style and text, a scaled-font record, per-codepoint and upright flags.

**The change.** Fifteen properties of `CssRect`/`CssRectWord` (`FirstLineStyle`, `OverflowWrapMinWidth`, `FirstLineText`,
`FontSizeScale`, `ScaledFontKind`, `UsesPerCodepointFont`, `IsUprightOrientation`, `SuppressWrapBefore`, `HyphenationCandidates`,
`PreSplitWord`, `Hyphenation{Suffix,Prefix}`, `PreOverflowWrapWord`, `OverflowWrap{Suffix,Prefix}`) read through a
`WordRareState` allocated at the first write of a non-default value, exactly like `CssBox`'s `BoxRareState`. Instance fields
264 -> 152 bytes; the multi-column benchmark's live heap ~1,516 -> ~1,336 MB.

**How the list was chosen** (the lesson of the `CssBox` change): not by what a word holds after layout but by counting
non-default *writes* per property over six benchmark documents. Written for most words and so left inline:
`AwaitsTheNextFragmentainer`, `PrecededByWordSeparator`/`WordSeparatorStyle`, `ScriptTag`, `UnicodeBreakBefore`, `BidiLevel`
(zero in LTR text but every word of an RTL one, and a byte anyway) and `NaturalSize` - which looks vertical-writing-only from its
remarks and is in fact written by `NaturalWordSize` for every word of the horizontal path (it was proxied first and
caught only by reading its callers).

**Not done.** `_preMirrorText`, `OriginalText` and the three logical script-category arrays are another ~40 bytes a word, but
`OriginalText` defaults to the constructor text and the arrays are non-null (all-`None`) for Latin words, so slimming them
changes what a word is born with rather than hiding unused state; left for a change that can check every reader.

**Evidence.** Release suite green (including new tests that a plain paragraph's words hold no rare state and a hyphenated one's do);
PDF words and drawings identical to main on 19 benchmark documents, 199 fuzz documents and all 220 showcase PDFs; 0-warning rebuild.
