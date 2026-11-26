# MathML WPT tiers, and the script DOM's view of `<math>`

The WPT runner gained a `mathml` module (`mathml/presentation-markup`, `mathml/relations`) in all three tiers: reftests, reftests with scripting, testharness. Baseline at the pinned commit: 83/491 reftests without scripting, 100/278 with it, 455/3639 testharness subtests. Floors are in `wpt-floors.json`, `wpt-floors-scripting.json` and `wpt-floors-dom.json`.

Three script-visible defects were found by running it:

- **A script could not reach anything inside `<math>`.** The page parser keeps an inline `<math>`'s markup as `MathSourceElement`s, which are not boxes, so `getElementById`, `children` and `outerHTML` saw an empty element. Worse, a page whose scripts changed anything was written back with `<math></math>`: every formula vanished. The parse a script reads (`HtmlParser.ParseDocument(..., scripting: true)`, `ScriptDocument.ParseDocument`) now builds boxes (`ParseMathAsBoxes`) and keeps a token's text as a child text node (`KeepMathTokenText` / `ParserState.KeepTokenText`) - the `ForeignText` shortcut is for the page's own parse only. The page's render parse is unchanged, so the performance work in `CssBox.ForeignText` still holds.
- **Self-closing foreign elements lost their end tag on serialize** (`<mspace/>`, `<path/>`): `ScriptSerializer` treated `IsSingle` as void, so the markup read back nested everything after the element. Only HTML elements are void.
- **`document.fonts` was not iterable**, so `[...document.fonts]` (used by the MathML tests' `loadAllFonts`) threw. It is a real, always-loaded, empty set; the page's `@font-face` rules are still loaded by the engine, not through script.

Trap: 22 scripting reftests that "passed" before did so because the formulas were missing from both the test and its reference. They now fail honestly (the feature-detection helper reports `mfrac` unsupported because `getBoundingClientRect` on a MathML element is 0): MathML elements are not CSS boxes, which is the next piece of work. The scripting floor was set to the honest number.

A reference of `about:blank` is now an empty page in the runner instead of "reference file missing".
