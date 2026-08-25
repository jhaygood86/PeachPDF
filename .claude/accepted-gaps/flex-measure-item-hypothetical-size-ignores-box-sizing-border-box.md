# Flex `MeasureItem`'s hypothetical-size measurement pass may ignore `box-sizing: border-box`

_Tracked as [#837](https://github.com/jhaygood86/PeachPDF/issues/837). Found via code audit while fixing
[#832](https://github.com/jhaygood86/PeachPDF/issues/832) (`ComputeCrossOffsets`'s stretch branch),
itself found while fixing [#815](https://github.com/jhaygood86/PeachPDF/issues/815) - the same bug
shape recurring at a third/fourth call site none of those issues named._

`CssLayoutEngineFlex.MeasureItem`'s "layout at hypothetical size" step converts a `hypothetical` outer
main-axis size back to a content-space value (`cssContentSize = Math.Max(0, hypothetical -
MainPaddingBorder(box))`) before temporarily setting `box.Width`/`Height` for a measurement-only
`PerformLayoutBlockified` call used to read the item's cross-axis dimension. Unconditionally subtracting
raw padding+border is only correct for `box-sizing: content-box` - the same class of bug already fixed
at #815 (`ResizeItem`/`ShrinkColumnItemToContentWidth`) and #832 (`ComputeCrossOffsets`).

**Not folded into #832's fix** because, unlike #832's confirmed and reproduced defect, this call site's
actual impact is unclear without its own investigation:

- For the explicit `flex-basis`/`width`/`height` branch, `hypothetical` is built a few lines earlier via
  `CssValueParser.ParseLength(...) + MainPaddingBorder(box)` - the identical box-sizing-blind assumption,
  in the opposite direction. For a `border-box` item the two likely cancel: `cssContentSize` recovers the
  original parsed value, which is already correct as a `border-box` item's `Width`/`Height` string (plain
  `CssBox` layout already treats an explicit `border-box` `Width`/`Height` as the outer size). This path
  is probably fine.
- For the content-measured `naturalMain`/`maxContent` branches (word-width measurement, container-fill
  fallback), there is no such compensating addition upstream, so subtracting raw padding+border to reach
  `cssContentSize` looks like a genuine under-sizing for a `border-box` item on this path specifically -
  the same shape #832 fixed in `ComputeCrossOffsets`.

Confirming which branch(es) are actually affected - and building a failing-then-passing regression test
the way #815/#832 each did - needs its own pass rather than diluting #832's scoped, confirmed fix.

See [issue #837](https://github.com/jhaygood86/PeachPDF/issues/837) for the full writeup.
