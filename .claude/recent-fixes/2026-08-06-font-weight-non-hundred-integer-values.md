# `font-weight` rejected non-hundred integer values (e.g. `550`)

Issue [#655](https://github.com/jhaygood86/PeachPDF/issues/655). Per [CSS Fonts Level 4](https://www.w3.org/TR/css-fonts-4/#font-weight-prop),
`font-weight`'s `<number>` grammar is `normal | bold | <number [1,1000]>` — any integer in that range,
not just multiples of 100. `font-weight: 550` was rejected at parse time and the whole declaration
dropped, before it ever reached `CssBox`'s cascaded style.

**Two independent enforcement points, both needed a fix:**

- **Layer A (CSS-OM shorthand/legacy path)**: `ValueExtensions.IsWeight`
  (`src/PeachPDF/CSS/Extensions/ValueExtensions.cs`), used by `FontWeightProperty`'s
  `WeightIntegerConverter`, hard-coded the legacy CSS2.1 set (`100, 200, ..., 900`) instead of a range
  check. Fixed to `value is >= 1 and <= 1000`.
- **Layer B (generated registry, used by inline styles and the cascade)**: `font-weight`'s
  `css-properties.json` entry deliberately left its integer clause unbounded (see the entry's old
  comment) on the assumption nothing downstream enforced a range either. That assumption was true but
  wrong to rely on — `int.TryParse` alone would happily store `font-weight: 99999`. The generator
  (`KeywordOrValueGrammar.BuildIntegerValueClause`) already supports a `min`/`max` narrowing clause for
  a `keyword-or-value` integer (same mechanism `column-count` already used) — added `"min": 1, "max":
  1000` to the `font-weight` entry rather than writing a new validator. No generator code changes
  needed; `Validate_FontWeight`/`Set_FontWeight` pick up the range automatically.

Both fixes needed because they're genuinely separate parsers — Layer A gates what a stylesheet/inline
`style=` string is even allowed to become a `StyleDeclaration` value, Layer B is what `CssUtils.SetPropertyValue`
calls when cascading that value onto a `CssBox`. Fixing only one leaves the other still spec-non-compliant
on its own path.

**Not changed:** `FontWeightResolver.Resolve(string, int)` (the `@page` margin-box raw-string overload)
still does a bare `int.TryParse` with no range check — but it only ever receives a value already filtered
through Layer A's `StyleDeclaration` parsing, so it's covered transitively rather than needing its own
check.

**Verified:** `ValueExtensions.IsWeight` (parser-level `FontProperty.cs` tests: `550` legal, `1001`/`0`
illegal), registry path (`CssUtilsTests.SetPropertyValue_FontWeight_NonHundredInteger_IsAccepted`/
`_OutOfRangeInteger_IsIgnored`), and typed-storage (`FontWeightFlexBasisTypedStorageTests.FontWeight_NonHundredIntegerValue_StoresParsedInteger`).
Full `PeachPDF.Tests` suite (net8.0): 8165 passed, 0 failed, 9 skipped (pre-existing, unrelated).
`dotnet build PeachPDF.slnx -t:Rebuild`: 0 warnings. Diff coverage: 100% (1 line, `ValueExtensions.cs`;
the `css-properties.json` range addition isn't itself instrumentable, but is exercised end-to-end by the
registry-path tests above).
