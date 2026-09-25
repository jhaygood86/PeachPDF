# `font-family: math` generic and the `<math>` UA font rule

Issue: font-family: math was an unknown family; `<math>` had no UA font rule.

**The load-bearing decision: `math` does not go through fontconfig on Linux.** The five other generics
ask fontconfig (`fc-match <generic>`), but a pattern for `math` matches no rule, and fontconfig answers with
the ordinary default sans family. That answer is installed, so it would always win over a real math font
later in the chain - the generic would silently mean "sans-serif". `GenericFontFamilyResolver.ResolveMathFamily`
therefore takes a per-platform *chain* and the first installed candidate wins; Chromium itself hardcodes
"Latin Modern Math" (Android excepted), which Windows and macOS do not ship, hence Cambria Math / STIX Two
Math behind it.

The chain lives in a pure function taking platform booleans and an `exists` delegate so every arm is
tested on any host (same shape as `ResolveSystemUiFamily`); the adapter constructor's existing
"not installed -> `DefaultFontResolver.DefaultFont`" correction covers the empty-chain case.

**Cambria Math is only reachable because discovery reads `.ttc` files** - see [font collections](2026-09-24-font-collections-are-discovered.md).

**Test trap:** `MathSmokeTests.NoMathTable_FallsBackGracefully` relied on `<math>` having no font rule so the
default (non-math) font exercised the fallback constants. With the UA rule a host that has a math font
installed would stop exercising that path, so the test now pins `math { font-family: serif }`.

**How the UA rule is verified:** layout-level (`GenericFontFamilyIntegrationTests`), reading the resolved
`FontAdapter.Font.Name` of the `<p>` and `<math>` boxes with `math` remapped to the bundled STIX Two Math -
an author-free `<math>` must resolve to it, a `<p>` must not, and an author rule must still override.
