---
name: webkit-legacy-properties
---

# -webkit- legacy properties (#2261)

Most of the aliases the issue listed were already in `VendorPropertyAliases` (background-clip/origin/size, border-radius, border-image, box-shadow, mask-* longhands, perspective, text-orientation), so `-webkit-background-clip: text` already worked. What was really missing, and is now done:

- `-webkit-mask` and `-webkit-box-decoration-break`: added to the alias table.
- `-webkit-column-break-before/after/inside`: not an alias (legacy `always`/`avoid` values), so own JSON entries with `aliasOf` and a `customSetter`, like `page-break-*`. `always` -> `column`, `avoid` -> `avoid-column`.
- The six accept-and-ignore properties are `"cssDataType": "unsupported"` entries.
- `-webkit-text-fill-color`: `DerivedStyle.ActualTextFillColor` replaces `ActualColor` for glyph fill, separators and leaders, and selection's transparent-text check. Shadows, decorations and borders still use `color`.
- `-webkit-text-security`: masks in `CssBox.AppendWordsFromText` (and the `::first-line` re-derive), length-preserving so word indices stay valid.
- `-webkit-box-reflect`: `FragmentPainter.Reflect.cs`, a fresh painter paints the fragment under a mirror matrix (a fragment is painted once per painter); optional gradient mask through tiles + `DrawImageAlphaMasked`. `url()` masks are not loaded.
- `initial-letter`: `DomParser.ApplyInitialLetter` floats the `::first-letter` box and sizes its font from cap-height metrics.

Traps found by running it:
- `DerivedStyle` caches `ActualFont`/em height "after the cascade", so setting `font-size` after reading the font left the float at the old size; `DerivedStyle.InvalidateFont()` exists for that one caller.
- The float exclusion ignores a negative `margin-bottom` (the accepted negative-margin float gap), so the letter's float is ended with a declared `height`, 0.05pt short of line `sink`'s bottom, not with a negative margin.
- `AllShorthandTests` excluded every `-webkit-box-*` name as legacy flexbox; `-webkit-box-reflect` is now carved out of that.

Not done: vertical writing modes, `initial-letter-align`/`-wrap`, `initial-letter` on a non-`::first-letter` box, `url()` masks for box-reflect.
Evidence: Engine.Tests suite green; page rendered through PDFium and MuPDF, which agree.
