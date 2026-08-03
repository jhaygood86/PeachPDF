# `color-mix()` silently dropped digit-leading hex operands (`#2563eb`)

Issue [#608](https://github.com/jhaygood86/PeachPDF/issues/608). A hex `color-mix()` operand only resolved
when letter-leading (`#e11d48`); a digit-leading one (`#2563eb`) silently failed, even though the same hex
string worked fine as a plain `color: #2563eb` value. Real-world relevant: Tailwind v4 compiles its
opacity-modifier syntax (`bg-[#2563eb]/50`) to `color-mix()`, so roughly half of all possible hex colors
(any with a leading digit) lost their opacity modifier.

**Root cause was upstream of `ColorFunctionExtensions`** (the issue's suggested starting point) — in
`CssValueParser.GetColorByName` (`Html/Core/Parse/CssValueParser.cs`), which resolves any color string that
isn't a bare `#hex`/`rgb()`/`rgba()` (named colors, `hsl()`, `oklch()`, `color-mix()`, ...). It called
`GetCssTokens(substring)` with the default `inValueContext: false`. `Lexer`'s `#`-handling branches on
`IsInValue`: `false` routes through `HashStart()`, which requires the *first* character after `#` to be a
name-**start** code point (CSS Syntax §4.3.4) — a letter passes, but a digit doesn't, so `#2563eb` fell
through to `NewDelimiter('#')` (a bare `#` token) instead of a `Hash` token, and the rest of the string
tokenized as unrelated number/ident tokens `ColorFunctionExtensions.ToResolvedColor` never recognized.
`true` routes through `ColorLiteral()` instead, which classifies purely on whether every character is a hex
digit (`IsHex()`), regardless of position — so both `#e11d48` and `#2563eb` come out as a single `Color`
token either way. This distinction (and the fix) is already documented on `GetCssTokens` itself, at
`Html/Core/Parse/CssValueParser.cs:474-476` — the comment was correct, `GetColorByName` just wasn't
following it.

**Fix:** `GetColorByName` now calls `GetCssTokens(substring, inValueContext: true)`. One-line functional
change; the rest of the diff is the doc comment explaining why.

**Verified:** added `ModernColorParsingTests.ColorMix_WithHexOperand_IsOpacityModifier` (theory over both a
letter-leading and digit-leading hex operand, asserting the resolved RGB and the halved alpha of the
Tailwind opacity-modifier shape `color-mix(in oklab, <hex> 50%, transparent)`). Confirmed the digit-leading
case actually fails without the fix (reverted the one-line change locally, re-ran — it fails with `Actual:
0` for R/G/B; re-applied the fix, passes) and that the letter-leading case already passed either way,
matching the issue's own description. Full `PeachPDF.Tests` suite (net8.0): 7659 passed, 0 failed, 9
skipped (pre-existing platform-specific skips, unrelated). `dotnet build PeachPDF.slnx -t:Rebuild`: 0
warnings.

**Not investigated further:** whether any other `GetCssTokens(...)` call site (default `inValueContext:
false`) has the same latent bug for a hex value nested inside a function argument. The other callers found
by grep either don't parse colors (grid/content/font-face grammars) or reconstruct a plain `#hex` substring
and re-enter `GetActualColor`'s dedicated `GetColorByHex` fast path (`TryGetColor`'s `str[idx] == '#'`
branch), which never goes through `GetCssTokens` at all — so none of them were exposed to this bug. If a
future caller needs `GetCssTokens` to tokenize a color nested inside another function's arguments, it needs
`inValueContext: true` for the same reason.
