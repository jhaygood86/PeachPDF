# `word-break: keep-all` has no distinct behavior from `normal`

Tracking issue: [#604](https://github.com/jhaygood86/PeachPDF/issues/604).

Per [CSS Text Module Level 3 §5.1](https://www.w3.org/TR/css-text-3/#word-break-property), `keep-all`
should disable word-breaking between CJK (Chinese/Japanese/Korean) characters that `normal` would
otherwise allow — a behavior distinct from both `normal` and `break-all`. `CssBox.cs`'s line-breaking
logic only special-cases `break-all` (`WordBreak == CssConstants.BreakAll || CommonUtils.IsAsianCharacter(rune)`);
`keep-all` is parsed and stored on the computed style but never checked anywhere in the break logic, so
it behaves identically to `normal` — CJK text still breaks between characters exactly as `normal` would.

`css-properties.json`'s `word-break` entry correctly lists `keep-all` as a stored, `@supports`-reported
keyword (it does parse and round-trip), but `docs/html-css-support.md`'s `word-break` row previously
listed it alongside `normal`/`break-all` without qualification, implying it changes rendering the way
`break-all` does; corrected to note it currently has no distinct effect.

**Deliberately out of scope.** Fixing this means adding a `keep-all` branch to the CJK line-breaking
check in `CssBox.cs` that suppresses the break `IsAsianCharacter` would otherwise force — a real
line-breaking behavior change, not a doc-accuracy fix.
