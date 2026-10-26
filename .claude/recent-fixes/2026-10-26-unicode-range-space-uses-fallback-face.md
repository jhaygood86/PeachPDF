# `unicode-range` and the inter-word space

_Landed 2026-10-26._

**The collapsible space between words was measured and drawn with the box's primary font** (`ActualFont`), which is the `unicode-range` face when it is first in the stack, so U+0020 came out in a face whose range excludes it (issue #1932). Spaces are not words, so the per-codepoint run splitter never saw them. `CssBox.ActualSpaceFont` now resolves U+0020 through `ActualFontForCodepoint` only when a family in the stack declares explicit ranges, and `CssUtils.WhiteSpace` (measure) and `FragmentPainter.PaintWordSeparator` (paint) both use it, so gap width and glyph agree. Boxes without ranged families keep `ActualFont`, so a font lacking a space glyph still gets no system-fallback space. Test: `UnicodeRangeLayoutIntegrationTests.SpaceOutsideRange_IsDrawnInFallbackFace_NotTheRangedFace`.
