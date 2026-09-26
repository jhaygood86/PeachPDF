# Line breaking across inline element boundaries still uses the old approximation

Where a line may end inside one box's text comes from the Unicode line breaking algorithm (UAX #14): `CssBox.AppendWordsFromText`
analyses the box's text once with `UnicodeLineBreaks.Find` and records the answer on each word (`CssRect.UnicodeBreakBefore`).
`CssLayoutEngine.HasOrdinaryWrapOpportunityBefore` uses it only for two words of the **same** box.

Two words of different inline boxes (`foo<b>bar</b>`, `<i>abc</i>/def`) are decided by the older rules: a break needs white space, a
hyphen, an ideograph or an emoji beside it, plus the table of opening and closing punctuation. So an element boundary can move a
wrap point, and the algorithm's no-break rules are not applied across it.

Fixing it means analysing the paragraph as one string with its inline boxes mapped back onto it, which is what
`CssBidiParagraphResolver` already flattens for bidi; the analysis has to skip `display: none` children exactly as the
inline-content predicates do. Tracked in [#1403](https://github.com/jhaygood86/PeachPDF/issues/1403).
