# PeachDrawing.Svg

A vector SVG backend for [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): draw shapes, gradients, clips,
text and images onto an `SvgCanvas` and save the result as an SVG document, with no HTML, CSS or PDF involved, and no
[PeachPDF](https://peachpdf.net/) reference needed. It is the sibling of the PDF writer
([PeachDrawing.PDF](https://www.nuget.org/packages/PeachDrawing.PDF/)) and the raster canvas
([PeachDrawing](https://www.nuget.org/packages/PeachDrawing/)), so drawing code written against `PeachDrawing.Core` can target
any of the three. Everything SVG can express stays vector: paths keep their curves, gradients stay gradients, clips stay clips,
glyphs are exact outlines (or selectable `<text>` with an embedded subset font on request), and PNG, JPEG, GIF, WebP and AVIF
images are embedded exactly as they came. It runs everywhere .NET runs, and is trimmable and Native AOT compatible.

```bash
dotnet add package PeachDrawing.Svg
```

**Status:** pre-1.0, versioned in lockstep with PeachPDF (the same number for every release); until 1.0 the API may change
between releases. Targets .NET 10 and later.

## Quick start

```csharp
using PeachDrawing.Core;
using PeachDrawing.Svg;

var context = new SvgRenderContext();                // registers every font installed on this machine
var composer = new SvgComposer(context, widthPoints: 480, heightPoints: 300);
var g = composer.Canvas;

g.DrawRectangle(context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255)), x: 20, y: 20, width: 200, height: 100);

var font = context.GetFont("Arial", size: 24, PaintFontStyle.Regular);
if (font is not null)
{
    g.DrawString("Hello, SVG", font, PaintColor.Black, new PaintPoint(20, 150), g.MeasureString("Hello, SVG", font));
}

await using var file = File.Create("out.svg");
composer.Save(file);                                 // or composer.ToSvg() for a string, or Save(TextWriter)
```

An `SvgComposer` is one SVG document (SVG has no pages): make one composer per image. Units are points, with the origin at the top left
and y growing downwards, as in `PeachDrawing.PDF`. The document's `width` and `height` are in `pt`.

## Options

```csharp
var options = new SvgComposerOptions
{
    TextMode = SvgTextMode.Text,                     // Outlines (default), or selectable <text> with an embedded subset font
    ConicGradients = SvgConicGradientMode.Css,       // Wedges (default, the same in every viewer) or a CSS conic-gradient()
    Background = PaintColor.White,                   // unset: transparent
    Precision = 2,                                   // decimal places in coordinates; fewer is smaller
    Title = "Quarterly chart",                       // written as <title> and <desc> for assistive technology
    Description = "Revenue by region",
};
var composer = new SvgComposer(context, 480, 300, options);
```

Use `context.AddFont(stream, "Family")` (TrueType, OpenType, WOFF or WOFF2) to get the same text on every machine.

## What each Canvas call becomes

| Canvas | SVG |
|---|---|
| `DrawRectangle`, `DrawPolygon`, `DrawPath`, `DrawLine` | `<rect>`/`<path>`/`<polygon>`/`<line>`, with fills and strokes (dashes, caps, joins, miter limits) |
| Linear, radial and conic gradient brushes, tile and hatch brushes | `<linearGradient>`, `<radialGradient>`, `<pattern>`; conic gradients as wedges or CSS |
| `PushClip`, `PushTransform` | `<clipPath>` groups and `transform` |
| `PushBlendMode`, layers, `BlurEffect`, `DropShadowEffect` | CSS compositing and `<filter>` |
| `DrawImage` and its variants | `<image>`, embedded as it came and defined once |
| `DrawString`, `DrawGlyphs` | glyph outlines (each glyph a `<use>` of one definition), or `<text>` |
| `AddLink`, `AddDestinationLink`, `AddDestination` | `<a href>` over an unpainted `<rect>` and anchors |
| `AddSemanticRegion` | role and name attributes for assistive technology |

## Limits

The only content that is not vector is what SVG cannot express: an effect a caller asks for as a bitmap
(`BeginRasterSurface`) is embedded as a PNG. A conic gradient drawn as `Wedges` is an approximation; `Css` is exact but only
browsers draw it. Canvas calls with no SVG meaning (tagged-PDF structure) are ignored.

## More

- [The PeachDrawing.Svg guide](https://peachpdf.net/peachdrawing-svg.html): the full Canvas-to-SVG mapping, outline versus selectable text, limitations.
- [API reference](https://peachpdf.net/api/PeachDrawing.Svg/index.html)
- [PeachDrawing.Core](https://www.nuget.org/packages/PeachDrawing.Core/): the `Canvas` API that every backend shares.

## Sponsorship

PeachPDF is free and open source. If it saves you time, you can [sponsor jhaygood86 on GitHub](https://github.com/sponsors/jhaygood86);
sponsors can get paid support, see [Sponsorship](https://peachpdf.net/sponsorship.html).

## License

BSD 3-Clause (see `LICENSE`).
