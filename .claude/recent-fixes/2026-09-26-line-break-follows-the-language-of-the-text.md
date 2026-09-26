# `line-break` follows the language of the text

`LineBreakOptions.Language` (a BCP 47 tag, `null` when unknown) rides on the existing `ParagraphStyle.LineBreak`, so `Paragraph` needed no
member of its own. `LineBreakAlgorithm.BuildUnits` reads it once (`IsChineseOrJapanese`: the primary subtag `ja`, `zh`, `yue` or `cmn`, any
case, so `jam` and `zhx` are not) and gates on it exactly what CSS Text 3 §5.2 gates: U+301C and U+30A0 for `normal` and `loose`, the centred
punctuation for `loose`, and the new breaks before a suffix and after a prefix. Small kana, iteration marks and the hyphen after an ideograph
are language-independent in the specification and stay that way. PeachPDF passes `CssBox.Language` from both callers of
`UnicodeLineBreaks.Find`: `AppendWordsFromText`, and `AllowsBreakAcrossBoxes` (the language of the box after the seam).

## What was found by running it, not by reading it

- **Chrome is the oracle, and the specification's prose is not enough.** A width-0 container makes a browser break at every opportunity, so one
  page of `Range` rectangles per language and strictness lists them all (a throwaway page served from localhost, not checked in). It showed that `loose` allows a break *between* two
  ellipses and never before a lone one, in every language: the old `IN` to `ID` remap was the bug behind "loose breaks before an ellipsis after
  Latin text", not only the missing language. It also showed which suffix and prefix characters really break: exactly the `PO`/`PR` ones of
  East Asian width, except the Latin-1 `°`, `±` and `¤`, which Chrome leaves alone (a `20°C` split after the `20` would be absurd). The
  exclusion is in the algorithm (`cp >= 0x100`), not the table, so the table stays the plain property. Chrome also gates the wave dash and the
  centred punctuation on Korean, and the hyphen after an ideograph on Chinese and Japanese, which the specification does not say; we follow the
  specification (Chinese and Japanese only; the hyphen in every language).
- **`PdfGenerateConfig.DefaultLanguage` never reached the words.** `PdfGenerator.SetContent` applied it after `SetHtml`, when every text box
  had already been cut into words, so it was inert for `hyphens: auto` too. It is now `HtmlContainerInt.DefaultLanguage`, set before parsing
  and read by `DomParser` where it reads `<html lang>`.
- **The suffix and prefix rule goes after LB22, not after LB18.** A first version put it right after LB18 and let a break through next to quotes, hyphens and
  inseparable characters (`￥-5`, `￥…`, `￥"`, a tab after `￥`); a review caught it and Chrome agrees with the later position on every one. It only has to
  override the number rules (LB23a, LB24, LB25).
- **A pair of inline boxes with no break between them is not moved as a unit.** `あいう<b>・</b>` in English overflows the line instead of
  wrapping `う・` together: the greedy loop only knows the seam before each word. That is how it always behaved (`foo<b>bar</b>`), unrelated to
  language, so the layout test asserts the dot stays on the kana's line rather than a wrapped result.
- **The bundled Noto Sans JP subset has almost no kana**, so layout tests with it fall back to a system CJK font whose widths depend on the
  machine, and a fragment from another font gets no break information. `assets/fonts/LineBreakTest.ttf` (generated, public domain) has every
  character in the tests one em wide, so a line 50pt wide holds exactly three of them.

## Table

`generate_segmentation_tables.py` sets bit 12 (`LineBreakWideOrAmbiguous`) on `PR` and `PO` characters whose East Asian Width is Ambiguous,
Fullwidth or Wide (19 of the 105 in Unicode 18), and only those, so the table gained a handful of ranges. The four Unicode conformance suites
still pass line for line: they run `Strict` with no language, where none of this applies.

## Not done

Korean (not in the specification's condition), and a per-character language across an inline boundary (one language is used for the two
words at a seam).

## Evidence

`PeachDrawing.Text.Tests` 1653 tests and `PeachPDF.Tests` 14331 (Release 14321), Debug and Release; diff coverage 100% of 845 lines; the build has no warnings. The `line_break_language` showcase rasterized with PDFium and
MuPDF (identical) and its HTML run in Chrome: every card breaks where PeachPDF breaks it.
