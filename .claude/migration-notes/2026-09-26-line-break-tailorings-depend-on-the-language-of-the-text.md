# `line-break` tailorings depend on the language of the text

**Before:** every `line-break` tailoring applied whatever the language of the text: with `normal` (which `auto` is) or `loose` the wave dash
U+301C and the katakana double hyphen U+30A0 could start a line even in an English paragraph, and `loose` also let a line start with a
middle dot, a colon, an exclamation mark or an ellipsis after Latin text (`wait…` could wrap as `wait` and `…`). The `loose` breaks before a
suffix such as `％` and after a prefix such as `￥` did not exist. `PdfGenerateConfig.DefaultLanguage` did not reach any of it, or the
automatic hyphenation of `hyphens: auto` either, in an HTML document: the words of every text box are cut while the document is parsed, and
the fallback language was applied afterwards.

**Now:** the language of the text (the `lang` attribute of the element or of an ancestor, else the one on `<html>`, else
`PdfGenerateConfig.DefaultLanguage`) decides the breaks CSS Text 3 limits to a Chinese or Japanese writing system:

- The wave dash and the katakana double hyphen may start a line in `normal` and `loose` only for `ja`, `zh` (any variant), `yue` and `cmn`.
  A document with Japanese text and no language at all no longer gets them: add `lang="ja"` (or set `DefaultLanguage`).
- `loose` lets a line start with the middle dot, the fullwidth colon, semicolon, exclamation and question marks and the double exclamation
  and question marks only in those languages, and, only there, lets a line end before a suffix (`％`, `℃`, `′` and other `PO` characters of
  East Asian width) and after a prefix (`￥`, `＄`, `№` and other `PR` characters of East Asian width). The degree, plus-minus and currency
  signs of Latin-1 are left out, as browsers leave them out.
- Small kana, iteration marks and a hyphen after an ideograph still start a line in `loose` whatever the language.
- `loose` no longer breaks before an ellipsis or other inseparable character that follows text (`wait…` stays whole); it breaks between two
  of them (`……`), as CSS Text 3 says.
- `hyphens: auto` now hyphenates a document with no `lang` when `PdfGenerateConfig.DefaultLanguage` is set, as documented.
