# The PeachDrawing.Core public API must not mirror a competitor's

Same standing rule as [[text-public-api-must-not-mirror-a-competitor]]: a maintainer of a commercial
competitor has already objected that another PeachDrawing-family library (PeachImage) looked "too similar"
to theirs. `PeachDrawing.Core` is a 2D drawing-surface abstraction (a `Canvas` to draw shapes/text/
images on, `Brush`/`Pen` to paint with, paint-ready value types), and the vocabulary this domain has used
for decades - GDI+/`System.Drawing`, Java2D, Cairo, WPF's `System.Windows.Media`, Skia - is far more
genuinely *generic* than `PeachDrawing.Text`'s situation (where SixLabors.Fonts was a narrow, direct
competitor in a much smaller naming space): `Brush`, `Pen`, `FillMode`, `LineCap`, `LineJoin`, `DashStyle`
are common nouns nobody owns, present in nearly every 2D graphics API predating any of ours. That lowers but
does not remove the collision risk, so the same discipline applies.

## The rules

Identical to [[text-public-api-must-not-mirror-a-competitor]]'s five rules - requirements first, structural
differences not renames, naming discipline, clean-room process, and the register below kept current with
every PR that adds public API. One difference: the maintainer's per-PR naming-review waiver
([[no-more-api-review-gates]]) was granted for `PeachDrawing.Text` specifically; it has not yet been
confirmed to extend to this package, so - unlike `PeachDrawing.Text`'s register, whose early slice the
maintainer did review - nothing in the register below has had that maintainer pass yet. Treat it as pending,
not as already covered by the waiver, until told otherwise.

## Register

The origin column is where the name and shape come from. "Ours" means we chose it because of what it does
here. Most of this surface did not originate as new public API design - it is `PeachPDF`'s own `Html/Adapters`
abstraction layer (already shaped this way, already reviewed once as internal code), made public by the
`PeachDrawing.Core` extraction (see the plan's step 5). The origins below describe where *that*
naming came from, which mostly predates this register.

### Drawing surface

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `Canvas` (`PushClip`, `PopClip`, `PushTransform`, `PopTransform`, `PushBlendMode`, `PopBlendMode`, `DrawRectangle`, `DrawPolygon`, `DrawPath`, `DrawLine`, `DrawString`, `DrawImage`, `CreateTile`, `GetGraphicsPath`, `PixelsPerPoint`) | The surface shapes/text/images are drawn to | `System.Drawing.Graphics`/Java2D `Graphics2D`'s push/pop clip-and-transform-stack shape, renamed `Canvas` (the HTML5 `<canvas>`/Android `Canvas` term) specifically to read as a generic drawing surface rather than a GDI+-specific "Graphics" object - deliberate, since this project's whole point is a surface that is not tied to any one backend |
| `RenderContext` (`GetPen`, `GetSolidBrush`, `GetLinearGradientBrush`, `GetRadialGradientBrush`, `GetConicGradientBrush`, `GetFont`, `GetColor`, `ImageFromStream`) | The per-render factory a `Canvas` is built from: resolves colors, builds cached paint objects, resolves fonts | Ours; "context" is the plain word for a factory/environment object bound to one render (see .NET's own `GraphicsContext`/`DeviceContext` family and CSS's own "rendering context" from the Canvas 2D API) |
| `Image` (`Width`, `Height`, `Interpolate`, `GetPixels`) | A drawable raster or vector image | `System.Drawing.Image`'s shape; `GetPixels` (→ `PixelBuffer`) is ours, added in this extraction's step 3 so a `Canvas` implementation never has to downcast to reach raw pixels |
| `GraphicsPath` (`Start`, `LineTo`, `ArcTo`, `AddMove`, `AddBezierTo`, `AddArc`, `CloseFigure`, `Transform`, `AddPath`, `FillMode`, `ClipToRect`, `Flatten`) | A recorded, backend-agnostic vector path | GDI+ `System.Drawing.Drawing2D.GraphicsPath`'s recorder shape (`StartFigure`/`AddLine`/`AddArc`/`AddBezier`/`CloseFigure`); `Flatten` (curve-to-line-segment tessellation) is GDI+'s own term for the same operation on its own `GraphicsPath` type, and `PathContour`/`GraphicsPath.Flatten`'s return shape is ours, added in this extraction's step 3 |
| `PathContour` (`Points`, `Closed`) | One flattened, closed-or-open polyline of a path | Ours |
| `CurveContour` (`Start`, `Commands`, `Closed`), `PathCommand` (`Kind`, `Control1`, `Control2`, `End`), `PathCommandKind` (`Line`, `Cubic`), `GraphicsPath.GetCurveContours` | One subpath of a path with its curves intact: a start point and the lines/cubic Béziers that follow | Ours; the sibling of `PathContour` for geometry that must stay curved. `PathCommand` (not `PathSegment`) so it cannot collide with `PeachPDF.Svg.PathSegment`, the same collision-avoidance reason as `PaintColor`; the line/cubic split is PDF 32000-1 §8.5.2's own `l`/`c` path-construction operators, and arcs never appear because the recorder already turns them into cubics |
| `Geometry.PolygonClipper.ClipToRect` | Clips a closed polygon to an axis-aligned rectangle | The Sutherland-Hodgman polygon-clipping technique (1974), long-published and unowned; the name is ours. Replaces two private copies (PeachDrawing and PeachPDF's PdfSharpCore) |

### Paint objects

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `Brush` (abstract base), `SolidBrush`, `LinearGradientBrush`, `RadialGradientBrush`, `ConicGradientBrush` | What a fill/stroke paints with, as a closed set of concrete data records | `System.Drawing.Brush`/`SolidBrush`/`LinearGradientBrush`/etc.'s names, restructured as a discriminated union of plain data (mirroring this codebase's own pre-existing `CssImage`/`CssImagePainter.Paint` pattern - see `docs/architecture.md` §7 - not a competitor's API) rather than GDI+'s opaque, backend-owned brush handles; `ConicGradientBrush` is CSS Images 4's `conic-gradient()` (GDI+ has no conic/sweep gradient) |
| `GradientStop` (`PaintColor`, `Position`), `GradientSpread` (`Pad`, `Repeat`) | One color stop of a gradient, and how it extends past its own ends | CSS Images 4 `<color-stop>` and `<color-hint>`; `GradientSpread`'s two values are CSS gradient repeat semantics (`Pad` is the plain `*-gradient()` default, `Repeat` is `repeating-*-gradient()`), deliberately not GDI+'s "wrap mode" vocabulary |
| `Pen` (`Width`, `MiterLimit`, `DashStyle`, `LineCap`, `LineJoin`, `DashPattern`, `DashOffset`, `Paint`) | How a stroke is drawn | `System.Drawing.Pen`'s member set almost exactly, since PDF stroke state (PDF 32000-1 §8.4.3) and GDI+ stroke state converge on the same small vocabulary independently; `Paint` (a `Brush`, replacing GDI+'s separate pen-with-brush constructor overload) is ours |
| `DashStyle`, `LineCap`, `LineJoin`, `FillMode` | Stroke dash pattern, line ending shape, line joint shape, and path fill rule | PDF 32000-1 §8.4.3 (line cap/join styles, dash array) and §8.5.3 (nonzero/even-odd fill rule); the same four concepts GDI+ also names this way, independently derived from the same PDF/PostScript imaging model both trace back to |
| `PaintColor` (`R`, `G`, `B`, `A`, `IsCmyk`, `C`, `M`, `Y`, `K`, `FromArgb`, `FromCmyk`, `FromName`, `FromHex`) | A resolved, paint-ready color, RGB or native CMYK | `System.Drawing.Color`'s ARGB shape and factory-method pattern (`FromArgb` etc.), extended with native CMYK (`IsCmyk`/`C`/`M`/`Y`/`K`) because `device-cmyk()` (CSS Color 5) must reach the PDF writer without an RGB round-trip - GDI+ has no CMYK color type at all. Named `PaintColor`, not `Color`, specifically to avoid colliding with `PeachPDF.CSS.Color` (the *parsed*, cascade-level CSS color type) in any file that references both - see this extraction's own step-5 rename-crisis notes for why that collision is a real, previously-hit problem, not a hypothetical one |
| `PaintBlendMode` | A PDF separable/non-separable blend mode | PDF 32000-1 §11.3.5's own 16 blend-mode names, which are themselves CSS Compositing and Blending Level 1's `mix-blend-mode` keywords; named `PaintBlendMode` for the same `PeachPDF.CSS.BlendMode` collision-avoidance reason as `PaintColor` |
| `PaintFontStyle` | The synthetic bold/italic bits a font is requested with | `System.Drawing.FontStyle`'s `[Flags]` shape (`Regular`/`Bold`/`Italic`/`BoldItalic`); named `PaintFontStyle` for the same `PeachPDF.CSS`-collision reason as `PaintColor`/`PaintBlendMode` (a box's raw `font-style` CSS keyword is a different, string-typed concept elsewhere in `PeachPDF`) |
| `FontPalette` | A COLR/CPAL color-font palette override | CSS Fonts 4 `font-palette` |
| `InkSpan` | A one-dimensional interval with visible ink, for `text-decoration-skip-ink` gap computation | Ours |
| `FontFaceDescriptors` | The `@font-face` descriptors a loaded face carries | CSS Fonts 4 `@font-face` descriptor set, mirroring `PeachDrawing.Text.AddOptions`'s own shape for the same descriptors |
| `GlyphPlacement` | One glyph's index and pen-relative position | Standard glyph-run vocabulary (DirectWrite/CoreText "glyph run", also `PeachDrawing.Text.Shaping.PlacedGlyph`'s sibling concept at the paint layer) |
| `ColorMatrix` | A 4x5 (`Matrix4x4` + `Vector4`) color transform, for `feColorMatrix`/CSS filter color operations | SVG Filter Effects `feColorMatrix`'s own 4x5 matrix convention; built purely on `System.Numerics` types, no PDF-specific wrapper |
| `TextHinting` | Whether raster-rendered text is fitted to the pixel grid by the font's own hinting | FreeType/DirectWrite "hinting" vocabulary; `None`/`Standard`/`Monochrome` are FreeType's own three rendering modes |

### Text, paragraph and colour-glyph drawing

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `CanvasParagraphExtensions.DrawParagraph`/`DrawGlyphRun` | Paint an already-laid-out `ParagraphLayout` / already-shaped `GlyphRun` onto any `Canvas` | "Draw" + the `PeachDrawing.Text` type each consumes (`Paragraph`, `GlyphRun`), matching `Canvas.DrawString`/`DrawGlyphs`'s own verb; extension methods rather than new abstract members so existing `Canvas` implementations are not broken |
| `ParagraphPaint`, `TextDecorations` (`Underline`, `Overline`, `LineThrough`) | The colour and lines a run of a paragraph is painted with | CSS Text Decoration's `text-decoration-line` keywords (`underline`/`overline`/`line-through`) as a `[Flags]` enum; "paint" is this package's existing word for colour-carrying values (`PaintColor`, `PaintPoint`) |
| `ColorGlyphs.ColorGlyphPainter`, `IColorGlyphTarget`, `CanvasColorGlyphTarget` | Walk a glyph's COLR/CPAL artwork into clipped fills; where those fills go; the target that draws them on any `Canvas` | OpenType COLR/CPAL's own vocabulary ("color glyph", "paint"); painter/target split is the usual visitor shape |
| `ColorGlyphPaint` (`SolidColorGlyphPaint`, `LinearColorGlyphPaint`, `RadialColorGlyphPaint`, `SweepColorGlyphPaint`) | A fully resolved fill handed to a target | COLR v1 `PaintSolid`/`PaintLinearGradient`/`PaintRadialGradient`/`PaintSweepGradient`, after palette resolution |
| `RasterRegion`, `RasterSurface`, `IntRect`, `PixelMath` | An offscreen pixel surface a `Canvas` hands out for raster effects, its pixel rectangle type, and shared 8-bit pixel arithmetic | "Region"/"surface" are standard graphics vocabulary; `IntRect` is `System.Drawing.Rectangle`'s shape reduced to what the raster code needs; the members are the ones PeachPDF's effects pipeline reads |
| `Canvas.TileCacheOwner`, `BeginRasterSurface`, `DrawRaster`, `TransformScale`, `CurrentTransform`, `PrefersRasterGroups`, `FlattensTransparency`, `InvisibleText`; `RenderContext.RasterizationDpi`/`TextHinting`/`TextStemDarkening`/`MaxRasterPixels`/`RasterAntiAliasing`/`LayoutUnitsPerPoint`/`CreateSvgGlyphPainter`; `ISvgGlyphPainter` | The optional hooks a backend overrides to join the raster-effects and SVG-glyph pipeline | Formerly `internal` (named for PeachPDF's own pipeline); now the documented contract - names kept, with `FormCacheOwner` renamed `TileCacheOwner` because it identifies the owner of tiles, not PDF forms |

### Geometry

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `Rect` (`X`, `Y`, `Width`, `Height`, `Left`, `Top`, `Right`, `Bottom`, `FromLTRB`) | An axis-aligned rectangle | `System.Drawing.RectangleF`'s member set, in `double` (not `float`) precision - deliberately: `System.Drawing.Common`/`SkiaSharp`/`SixLabors.*` are dependencies this repo has never taken (see the plan's own survey), and CSS layout needs full `double` precision GDI+'s `float` doesn't give |
| `PaintPoint` (`X`, `Y`) | A 2D point | `System.Drawing.PointF`'s shape, `double`-precision for the same reason as `Rect`; named `PaintPoint` (not `Point`) to avoid colliding with `PeachPDF.CSS.Point` (a distinct, cascade-level CSS value type) in the same file - the same collision-avoidance reason as `PaintColor`/`PaintBlendMode`/`PaintFontStyle` |
| `Size` (`Width`, `Height`) | A 2D extent | `System.Drawing.SizeF`'s shape, `double`-precision; no colliding `PeachPDF.CSS.Size` exists, so no disambiguating prefix was needed |
| `Matrix3x2Extensions` (`Then`, `TryInvert`, `RebaseOrigin`) | Composition helpers for `System.Numerics.Matrix3x2`, the package's 2D affine transform type | `System.Numerics.Matrix3x2` itself is the BCL type (adopted per the plan's "drop a type with a real BCL equivalent" rule); `Then` and `RebaseOrigin` are ours (a named alias for row-vector `a * b` composition, and a fixed-point re-anchoring helper CSS `transform-origin` needs at paint time); `TryInvert` re-derives `RMatrix`'s original explicit not-finite/near-singular check rather than trusting `Matrix3x2.Invert`'s own laxer one - see this extraction's own migration notes for why |

### Fonts and network (types that travel with `RenderContext`)

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `Font` (abstract), `FontFamily` (abstract) | A resolved, sized typeface ready to draw with, and the family it was matched from | `System.Drawing.Font`/`FontFamily`'s names; deliberately still a size-bound "font" object (unlike `PeachDrawing.Text.Typeface`, which is size-free) because this is the paint-ready object a `Canvas.DrawString` call actually needs, not the matching-layer type |
| `TypefaceFont` (sealed, concrete `Font`) | A `Font` built directly from a `Typeface` + size, with no backend-specific wrapper underneath | Ours; named for exactly what it is (a `Font` that is nothing but a `Typeface` at a size) - the plain, no-adjective name a reader reaches for once the wrapped/adapted alternative (`PeachPDF.Adapters.FontAdapter`, backend-internal) already has the qualified one |
| `RUri` | A `System.Uri` wrapper with `data:`-URI-safe handling | Ours; wraps `System.Uri` rather than replacing it, adding only the `data:`-URI bypass pre-.NET-10 needs (see its own doc remarks) |
| `RNetworkResponse` (`ResourceStream`, `ResponseHeaders`) | The result of resolving a resource URI | Ours; an HTTP-response-shaped record (stream + headers) since that is the shape every one of the four network loaders' results already has |

Every name above was chosen either because it already existed as `PeachPDF`'s own internal
`Html/Adapters`/`Network` naming (predating this register, not new design done under this rule) or because
it is standard, decades-old 2D-graphics/PDF/CSS vocabulary no single competitor owns. Rule 5's maintainer
naming-review has not yet been done for this table - see the note above the register.
