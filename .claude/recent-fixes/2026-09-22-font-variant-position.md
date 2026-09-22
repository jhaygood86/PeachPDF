# `font-variant-position`, and why its synthesis reads the font rather than a constant

Added CSS Fonts 4 `font-variant-position: normal | sub | super`. Needed on its own merits, but the
immediate reason was css-gcpm-3's default UA stylesheet for footnotes, which contains
`@supports (font-variant-position: super) { ::footnote-call { …; vertical-align: baseline;
font-size: 100%; font-variant-position: super } }`. Registering the property is what makes that
`@supports` condition true - the oracle is the generated `CssPropertyRegistry.SupportsDeclaration` -
and at that point a footnote call renders at full size on the baseline and depends entirely on real
sub/superscript glyphs. So "parse it but don't render it" was never an option here: it would have
turned every footnote call into a plain digit.

## The load-bearing find: OS/2 already had the synthesis geometry, unused

`OpenTypeFontTables.cs` has parsed `ySubscriptXSize`/`YSize`/`XOffset`/`YOffset` and the superscript
quartet for as long as the OS/2 reader has existed, and nothing read them. Those fields exist in the
spec precisely so a UA can build a synthesized sub/superscript, so the fallback follows the type
designer's own intent per face instead of one ratio imposed on all of them - strictly better than
`font-variant-caps`' hand-tuned `SmallCapsFontScale = 0.72`, which its own doc comment admits was
"tuned by eye". The hardcoded ratios that remain are only for a font that leaves the fields at zero.
This is the third time the "check whether the primitive already exists, unused" reflex has paid off
here (`XForm`, `PdfSoftMask`, now these).

Measured, not assumed: Source Sans 3 and Source Code Pro both state `600/1000` scale with a
`350/1000` superscript offset; STIX Two Math states `500/1000` and `500/1000`. Byte-inspecting the
bundled fonts also decided the test fixtures - Source Sans 3, Source Code Pro *and*
`gsubtest-lookup3.otf` all carry real `sups`/`subs`, so the synthesis path needed a font that does
**not**, and STIX Two Math (28 GSUB features, none positional) is the one bundled face that fits.
Do that check before assuming a fixture exercises either path.

## The trap that would have been silent

`CssBox.ResolveWordFont` used to read `word.FontSizeScale == 1.0 ? ActualFont : ActualSmallCapsFont`
- i.e. it inferred *which* scaled face from the fact that a scale existed at all. That works only
while exactly one synthesis can ask for a scale. Adding a second meant the scale alone could no
longer answer, so `CssRect.ScaledFontKind` now names the asker and `ResolveWordFont` switches on it.
This is worth knowing because getting it wrong fails quietly: measurement and paint both go through
this one resolver specifically so they can never disagree about which face a word was drawn in, so a
wrong answer shows up as a mis-aligned baseline in a PDF, not as a failing assertion.

## Deliberately not done

- The baseline shift is applied in `FragmentPainter.Text`'s per-word `baselineAdjust`, **not** in
  `ApplyVerticalAlignment`. CSS Fonts 4 says these glyphs "have no effect on line-height and other
  box characteristics", and `vertical-align: super` shifts the whole inline box within its line and
  does grow the line box. Pinned by a test that compares word height with and without the property.
- No SVG synthesis, and small-caps synthesis wins when both would apply on one element - see
  `.claude/accepted-gaps/font-variant-position-synthesis-scope.md` (issue #1266).

## Evidence

Full suite green (13,201 passed / 9 skipped, net8.0). 13 new tests, split deliberately between the
real-GSUB path (glyph-index comparison on Source Sans 3 - advance widths can legitimately coincide,
so width assertions prove nothing) and the synthesized path (OS/2-derived scale and signed offset on
STIX Two Math). One pre-existing test changed: `CssFontWithSlashAndContent` counted the longhands the
`font` shorthand expands to, which is now 12 rather than 11 - the intended CSS Fonts 4 §7.7
consequence, not a regression. The showcase was rasterized through both PDFium and MuPDF and the two
agree: real and synthesized superscripts both sit raised and smaller, subscripts lowered and smaller.
