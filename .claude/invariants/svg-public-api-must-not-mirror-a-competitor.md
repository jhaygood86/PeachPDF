# The PeachDrawing.Svg public API must not mirror a competitor's

Same standing rule as [[core-public-api-must-not-mirror-a-competitor]],
[[text-public-api-must-not-mirror-a-competitor]] and [[peachdrawing-public-api-must-not-mirror-a-competitor]], scoped to
`PeachDrawing.Svg`'s own new public surface - the types the package adds on top of `PeachDrawing.Core`. There is no incumbent
to echo here as SixLabors was for the imaging packages, but the vocabulary of "write a 2D drawing as SVG" is the vocabulary of
`System.Drawing`, SkiaSharp's `SKSvgCanvas`, `Svg.NET`, `SvgSharp` and the SVG spec's own DOM (`SvgDocument`, `SvgWriter`,
`SvgElement`), and those are exactly the names a "too similar" objection would be raised against. Reading this file is the
precondition for adding a public type, member or option to the package.

## The rules

Identical to [[core-public-api-must-not-mirror-a-competitor]]'s five rules, and the same pending maintainer-review status as
[[peachdrawing-public-api-must-not-mirror-a-competitor]]: the naming-review waiver ([[no-more-api-review-gates]]) was granted for
`PeachDrawing.Text` specifically, so treat the register below as pending, not covered, until told otherwise. The shape to follow is
the one the PDF sibling already settled on: `Svg<Base>` for the backend's own implementation of a `PeachDrawing.Core` base
(`SvgCanvas`, `SvgRenderContext`), and `Svg<Composer...>` for the document-level surface, mirroring `PdfComposer`. Do **not** name
anything `SvgDocument`, `SvgWriter`, `SvgElement`, `SvgGraphics` or `SvgBuilder`.

## Register

| Public name | Role | Origin of the name and shape |
|---|---|---|
| `SvgCanvas` (sealed, extends `Canvas`) | The concrete drawing surface that writes each call as SVG | `Canvas` (already registered) with the `Svg` backend prefix - the `<Backend><Base>` shape of `PdfCanvas` and `RasterCanvas` |
| `SvgRenderContext` (extends `RenderContext`, subclassable) | Fonts, image decoding and the layout-unit scale an `SvgCanvas` draws with; a host derives from it for its own font aliases and resource loading | `RenderContext` (already registered) with the backend prefix, as `PdfRenderContext`/`RasterRenderContext`; `PixelsPerPoint`, `AddFont`, `OnFontFamilyAdded`, `ResolveVariationAxes` are the same members, with the same meaning, as the PDF context's |
| `SvgComposer`, `SvgComposerOptions` | One SVG document - its canvas, size and writing - and what is written into it. There is no page type: SVG has no pages | `Composer` is this repository's own name for the document-level assembler (`PdfComposer`, `PdfComposerOptions`), chosen over `Document`/`Writer`/`Builder` precisely because those are the incumbents' words |
| `SvgComposer.Canvas`, `.WidthPoints`, `.HeightPoints`, `.RenderContext`, `.Options`, `.Save(Stream)`, `.Save(TextWriter)`, `.ToSvg()` | The canvas and size, and writing it | `Canvas`/`WidthPoints`/`HeightPoints` mirror `PdfComposerPage`; `RenderContext`/`Options` mirror `PdfComposer`; `Save` mirrors `PdfComposer.Save` and `RasterCanvas.Save`; `ToSvg` names what it returns |
| `SvgComposerOptions.TextMode`, `.Precision`, `.Title`, `.Description`, `.Background` | What is written | Ours; `Title`/`Description` are the SVG element names, `Precision` is the number of decimals, `Background` is the page's backdrop colour |
| `SvgTextMode` (`Outlines`, `Text`) | Glyph outlines versus selectable `<text>` | Ours; each value names what is written |
| `SvgConicGradientMode` (`Wedges`, `Css`), `SvgComposerOptions.ConicGradients` | How a conic gradient is written: a fan of wedges, or a CSS `conic-gradient()` in a `foreignObject` | Ours; `Wedges` and `Css` name what is written, `ConicGradients` the option's subject |

Everything else in the assembly is `internal` (`SvgDefs`, `SvgImage`, `SvgTileImage`, `SvgGraphicsPath`, `SvgCanvasFont`,
`SvgCanvasFontFamily`, `SvgFontPackager`), and `PeachDrawing.Svg.Tests` alone is granted `InternalsVisibleTo`; a test pins both.
