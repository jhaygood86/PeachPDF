# `system-ui` was the one generic family that ignored the host's own fontconfig

`PdfSharpAdapter`'s constructor resolves five generic families through
`LinuxSystemFontResolver.ResolveGenericFamily` on Linux, then hardcoded `system-ui` to
`DefaultFontResolver.DefaultFont` on every platform — including the one where the line immediately
above it was asking fontconfig. Chromium delegates `system-ui` to fontconfig on Linux, and
fontconfig has shipped a `system-ui` alias since 2.13.1, so there was nothing to work around.

The measurable consequence is line-height, not glyph shape: on a host with the common deployment
font set, fontconfig answers FreeSans where the default is Liberation Sans, and the two disagree on
ascent+descent by 11.7% (1.000em vs 1.117em). `system-ui` is what generated header and footer bands
are drawn in, so every band was 11.7% taller than Chromium's.

## What running it turned up

- **The fallback arm cannot be tested in situ.** `fontconfigFamily is null || !IsFontExists(...)`
  never fires on a real machine — fontconfig answers, and its answer is installed. The decision is
  now `PdfSharpAdapter.ResolveSystemUiFamily(fontconfigFamily, fontExists)`, a pure two-argument
  function the constructor calls, so all three arms are directly testable. Same reasoning
  `DefaultFontFallbackTests` already uses when it tests against a synthetic default-font name rather
  than the real one.
- **The two doc pages already contradicted each other**, and it would have been easy to fix only the
  one this change touches. `docs/html-css-support.md` described `system-ui` as fontconfig-delegated
  on Linux (which the code now is); `docs/usage-examples.md`'s per-platform table and the prose
  under it described the hardcoded behaviour. Both now say the same thing.
- **No process is spawned.** Worth recording because it looks like it might:
  `LinuxSystemFontResolver.ResolveGenericFamily` P/Invokes `libfontconfig.so.1` directly
  (`FcPatternCreate`/`FcConfigSubstitute`/`FcFontMatch`) rather than shelling out to `fc-match`, and
  the string handed to it is a hardcoded literal, never anything from the document being rendered.

## Evidence

`"system-ui"` added to the three existing generic theories
(`LinuxSystemFontResolverTests.ResolveGenericFamily_DoesNotThrow` /
`_OnLinux_ReturnsARealInstalledFamilyName`, and
`GenericFontFamilyIntegrationTests.Generic_OnLinux_ResolvesToARealInstalledFontconfigFamily`), which
is where the other five are covered, plus three fixtures for `ResolveSystemUiFamily`'s own arms
(no fontconfig answer, an answer that is not installed, and an answer that is — the contrast case,
without which the first two also pass if the mapping always fell back). Full suite green on net8.0,
0 build warnings.
