# The engine owns its face-style types; the XFont-layer caches left it

Second step of the engine extraction (after [the infrastructure decoupling](2026-09-25-font-engine-infrastructure-decoupled-from-pdfsharpcore.md)):
`Fonts/` and `Text/` no longer reference `PeachPDF.PdfSharpCore` or `PeachPDF.CSS` at all, and a source-scan test
(`EngineIndependenceTests`, invariant `fonts-the-engine-must-not-reference-the-rest-of-peachpdf`) keeps it that way.

**What changed**
- `XStyleSimulations` is deleted; the engine's `SyntheticStyle` (`None/Bold/Italic/BoldItalic`) replaces it everywhere.
- The engine tells faces apart by `FaceStyle` (bold and/or italic; same bit values as `XFontStyle`). `XFontStyle` stays
  the PDF layer's type and converts once, through `XFontStyleExtensions.ToFaceStyle`, where `XFont` builds a request.
  Underline/strikeout are drawing decorations, so the mask drops them.
- `OpenTypeDescriptor` has one constructor, `(key, name, face)`. The old ones took an `XFontStyle` and `XPdfFontOptions`
  that were never read, and an `XFont`; the `XFont`-based one moved to its two callers.
- `FontResolvingOptions.ComputeTypefaceKey` owns the typeface cache key (it was `XGlyphTypeface.ComputeKey`, which the
  engine's `FontFactory` had to call back into).
- `FontDescriptorCache` and `GlyphTypefaceCache` moved to `PdfSharpCore/Drawing`: they are keyed on and hold `XFont` and
  `XGlyphTypeface`. `FontResolver` used to hold a `Dictionary<string, XGlyphTypeface>` per instance so a custom family's
  glyph typefaces died with the resolver; that per-instance cache is now a `ConditionalWeakTable<FontResolver, ...>` in
  `GlyphTypefaceCache`, which keeps exactly the same lifetime (weak against the resolver) without the engine naming the type.
- `CssUnicodeBidiMapping` moved from `Text/Bidi` to `Html/Core/Utils`: it maps CSS `unicode-bidi`/`direction` onto the
  engine's `BidiExplicitPush`, so it is the HTML/SVG layers' half of the integration, not the engine's.

**Deliberately not done:** `XFont`, `XGlyphTypeface` and the resolver still exist and still carry the request through the
engine as `FontResolvingOptions`. The size-free typeface model that replaces them is the next step; re-deriving it here
would have meant renaming it twice.
