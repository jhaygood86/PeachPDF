# Font sizes are requested in layout units (issue 2019)

`RenderContext.GetFont(size)` takes layout units (points x `LayoutUnitsPerPoint`) and builds the face at `size / LayoutUnitsPerPoint`
points; `Font.Size` is therefore true points. The in-flow path (`DerivedStyle.ActualFont`) handed it true points, so at a layout scale
other than 1 every font was 1/scale too small (0.75 at `PixelsPerPoint` 4/3, `PixelsPerInch` 96) while boxes were right. The margin-box
path (`MarginBoxRenderer.BuildFont`) already did `sizePt * ppp`.

**Fix:** `CssBox.GetCachedFont`/`GetCachedFontForCodepoint` take points and multiply by the layout scale before calling
`FontFamilyResolver`. Everything that had been multiplying `GetEmHeight()`/`GetRemHeight()` by ppp to "undo device scaling" (it only
cancelled the bug) now takes them as true points: `CssValueParser`, `CssBox.StyleProperties`, `DerivedStyle` (parent/rem size, `lh`
ratio), `DomParser` (page length context), `CssBoxMath`, `CssImagePainter`.

**Traps found by running it:**
- `Canvas.GetTextOutline` builds in the units of the origin it is given (layout units); a path reaches the PDF canvas in points.
  Text clip (`background-clip: text`) and text stroke relied on the font being 1/ppp small to hide that; they now scale the outline by
  `1 / PixelsPerPoint`.
- Inline SVG builds its fonts in user units straight through `FontFamilyResolver`; `DrawString` divides position and font by ppp but
  the SVG path/rect primitives do not, so SVG text was 1/ppp small *and* misplaced. `SvgRenderer.DrawUserText` scales by ppp around
  `DrawString`. Other non-path primitives in `SvgRenderer` (e.g. `DrawImage`, `PushClip(Rect)`) were not audited at ppp != 1.
- Ascent/line height round to whole layout units, so a baseline can differ by under a point between scales; tests allow 0.75pt.

Evidence: `LayoutScaleTextSizeTests` (text width in points equal at ppp 1 and 96/72 for px/pt/rem/keyword/Arial/em/small-caps) and
`LayoutScalePdfTextSizeTests` (SVG text and MathML size/position equal at `PixelsPerInch` 72 and 96). Engine, PeachPDF, PeachBrackets and
Svg suites green in Release; solution rebuild warning-free.
