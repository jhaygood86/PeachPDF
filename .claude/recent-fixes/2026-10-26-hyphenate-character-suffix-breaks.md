# A hyphenation split is a break opportunity whatever the hyphenate-character is (#1933)

`TryHyphenateWord` left the suffix's `UnicodeBreakBefore` null, so the flow re-ran UAX #14 over `prefix+suffix`. That only allows a break after the default `-` (class HY); with `hyphenate-character: "→"` (AL) or `""` the suffix stayed on the prefix's line and the word overflowed. The suffix now sets `UnicodeBreakBefore = true`: the split itself is the opportunity.

Covered by `HyphenateCharacter_NonBreakingGlyph_StillBreaksTheLine` in `HyphenationControlPropertiesIntegrationTests`.
