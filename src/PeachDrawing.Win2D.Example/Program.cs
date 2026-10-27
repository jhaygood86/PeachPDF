// PeachDrawing.Win2D.Example: draws a few scenes with PeachDrawing.Win2D alone and writes each one as a PNG file (Windows only).
//
//   dotnet run --project src/PeachDrawing.Win2D.Example [outputDirectory]
//
// Every scene is ordinary PeachDrawing.Core drawing code - the same calls would draw onto a PDF page or an SVG - drawn by Win2D into an
// offscreen bitmap. The last scene shows links: the page records them, and a click at a position is looked up in the link map.

using System.Numerics;
using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.Core.Geometry;
using PeachDrawing.Win2D;

var outputDirectory = args.Length > 0 ? Path.GetFullPath(args[0]) : Directory.GetCurrentDirectory();
Directory.CreateDirectory(outputDirectory);

// A software device, so the output is the same on every machine; pass a CanvasDevice (or nothing) to use the GPU.
using var context = Win2dRenderContext.CreateSoftware();
await using (var fontStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
{
    await context.AddFont(fontStream, "Source Sans 3");
}

await Scene("shapes", 480, 300, Shapes);
await Scene("gradients", 480, 300, Gradients);
await Scene("conic-gradients", 480, 200, ConicGradients);
await Scene("clips-and-transforms", 480, 300, ClipsAndTransforms);
await Scene("compositing", 480, 300, Compositing);
await Scene("blend-inside-a-clip", 480, 160, BlendInsideAClip);
await Scene("patterns-and-images", 480, 300, PatternsAndImages);
await Scene("text", 480, 300, Text);
await Scene("links", 480, 120, Links);

return 0;

async Task Scene(string name, double width, double height, Action<Canvas, Win2dRenderContext> draw)
{
    using var composer = new Win2dComposer(context, width, height, new Win2dComposerOptions { Background = PaintColor.White, Dpi = 192 });
    draw(composer.Canvas, context);

    var path = Path.Combine(outputDirectory, name + ".png");
    await using (var file = File.Create(path))
    {
        await composer.SaveAsync(file);
    }

    Console.WriteLine($"Wrote {path}");
}

static PaintColor Rgb(int r, int g, int b, int a = 255) => PaintColor.FromArgb(a, r, g, b);

static void Shapes(Canvas g, Win2dRenderContext c)
{
    // Fills and strokes: a rounded rectangle, a circle, a star with an even-odd hole, and a heart from cubic Béziers.
    var card = g.GetGraphicsPath();
    card.AddRoundedRectangle(new Rect(20, 20, 200, 120), 18);
    g.DrawPath(c.GetSolidBrush(Rgb(255, 239, 213)), card);
    g.DrawPath(PenOf(c, Rgb(205, 133, 63), 3), card);

    var circle = g.GetGraphicsPath();
    circle.AddCircle(300, 80, 55);
    g.DrawPath(c.GetSolidBrush(Rgb(30, 144, 255, 200)), circle);

    var star = g.GetGraphicsPath();
    star.AddStar(420, 80, 50, 22, 5, -Math.PI / 2);
    star.FillMode = FillMode.EvenOdd;
    star.AddCircle(420, 80, 12);
    g.DrawPath(c.GetSolidBrush(Rgb(220, 20, 60)), star);

    var heart = g.GetGraphicsPath();
    heart.Start(120, 250);
    heart.AddBezierTo(40, 200, 60, 150, 120, 180);
    heart.AddBezierTo(180, 150, 200, 200, 120, 250);
    heart.CloseFigure();
    g.DrawPath(c.GetSolidBrush(Rgb(255, 105, 180)), heart);
    g.DrawPath(PenOf(c, Rgb(139, 0, 70), 2.5), heart);

    // Pen styles: caps, joins and dash presets stay stroke attributes.
    var y = 175.0;
    foreach (var (cap, dash) in new[] { (LineCap.Butt, DashStyle.Solid), (LineCap.Round, DashStyle.Dash), (LineCap.Square, DashStyle.Dot), (LineCap.Butt, DashStyle.DashDot) })
    {
        var pen = PenOf(c, Rgb(40, 40, 40), 8);
        pen.LineCap = cap;
        pen.DashStyle = dash;
        g.DrawLine(pen, 250, y, 460, y);
        y += 22;
    }

    var zigzag = g.GetGraphicsPath();
    zigzag.Start(250, 270);
    zigzag.LineTo(300, 240);
    zigzag.LineTo(350, 270);
    zigzag.LineTo(400, 240);
    var joined = PenOf(c, Rgb(0, 100, 0), 10);
    joined.LineJoin = LineJoin.Round;
    g.DrawPath(joined, zigzag);
}

static void Gradients(Canvas g, Win2dRenderContext c)
{
    // Linear with several stops, including a translucent one.
    var linear = c.GetLinearGradientBrush(new PaintPoint(20, 0), new PaintPoint(220, 0),
        [(Rgb(255, 94, 98), 0), (Rgb(255, 195, 113), 0.5), (Rgb(255, 195, 113, 0), 1)]);
    g.DrawRectangle(c.GetSolidBrush(Rgb(235, 235, 235)), 20, 20, 200, 100);
    g.DrawRectangle(linear, 20, 20, 200, 100);

    // Repeating linear.
    var repeating = c.GetLinearGradientBrush(new PaintPoint(260, 20), new PaintPoint(300, 60),
        [(Rgb(30, 30, 30), 0), (Rgb(230, 230, 230), 1)], isRepeating: true);
    g.DrawRectangle(repeating, 260, 20, 200, 100);

    // Elliptical radial with an off-centre focus.
    var radial = c.GetRadialGradientBrush(new PaintPoint(120, 210), 100, 60,
        [(Rgb(255, 255, 255), 0), (Rgb(65, 105, 225), 0.6), (Rgb(25, 25, 112), 1)], focalCenter: new PaintPoint(90, 195));
    g.DrawRectangle(radial, 20, 150, 200, 120);

    // Conic gradient (a fan of wedges - SVG has no conic gradient), clipped to a circle.
    var conic = c.GetConicGradientBrush(new PaintPoint(360, 210), 70,
        [Rgb(255, 0, 0), Rgb(255, 255, 0), Rgb(0, 200, 0), Rgb(0, 200, 255), Rgb(120, 0, 255), Rgb(255, 0, 0)],
        [0, Math.PI * 0.4, Math.PI * 0.8, Math.PI * 1.2, Math.PI * 1.6, Math.PI * 2]);
    var disc = g.GetGraphicsPath();
    disc.AddCircle(360, 210, 70);
    g.DrawPath(conic, disc);

    // A gradient used as a pen.
    var gradientPen = c.GetPen(c.GetLinearGradientBrush(new PaintPoint(260, 0), new PaintPoint(460, 0), [(Rgb(0, 0, 0), 0), (Rgb(255, 0, 128), 1)]));
    gradientPen.Width = 6;
    g.DrawLine(gradientPen, 260, 140, 460, 140);
}

static void ConicGradients(Canvas g, Win2dRenderContext c)
{
    // The same two conic gradients in both files: a large disc, and a ring stroked with one. The "-css" file asks for CSS conic-gradient()
    // in a foreignObject (exact in a browser, the wedges anywhere else); the other is the wedges, which every viewer draws.
    var stops = new[] { Rgb(255, 0, 0), Rgb(255, 220, 0), Rgb(0, 200, 0), Rgb(0, 190, 255), Rgb(120, 0, 255), Rgb(255, 0, 0) };
    var angles = new[] { 0, Math.PI * 0.4, Math.PI * 0.8, Math.PI * 1.2, Math.PI * 1.6, Math.PI * 2 };

    var disc = g.GetGraphicsPath();
    disc.AddCircle(110, 100, 90);
    g.DrawPath(c.GetConicGradientBrush(new PaintPoint(110, 100), 90, stops, angles), disc);

    var ring = g.GetGraphicsPath();
    ring.AddCircle(350, 100, 70);
    var pen = c.GetPen(c.GetConicGradientBrush(new PaintPoint(350, 100), 80, stops, angles));
    pen.Width = 22;
    g.DrawPath(pen, ring);
}

static void ClipsAndTransforms(Canvas g, Win2dRenderContext c)
{
    var stripes = c.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(20, 20),
        [(Rgb(255, 215, 0), 0), (Rgb(255, 215, 0), 0.5), (Rgb(25, 25, 112), 0.5), (Rgb(25, 25, 112), 1)], isRepeating: true);

    // A clip to a circle: the stripes are cut to it exactly (a clip path, not a bitmap mask).
    var circle = g.GetGraphicsPath();
    circle.AddCircle(110, 100, 80);
    g.PushClip(circle);
    g.DrawRectangle(stripes, 0, 0, 240, 200);
    g.PopClip();

    // Everything except a rectangle.
    g.PushClip(new Rect(250, 20, 210, 160));
    g.PushClipExclude(new Rect(300, 60, 110, 80));
    g.DrawRectangle(c.GetSolidBrush(Rgb(46, 139, 87)), 250, 20, 210, 160);
    g.PopClip();
    g.PopClip();

    // Transforms: a rotated, scaled group of shapes drawn in its own coordinates.
    g.PushTransform(Matrix3x2.CreateRotation((float)(Math.PI / 12)) * Matrix3x2.CreateTranslation(60, 190));
    g.PushTransform(Matrix3x2.CreateScale(1.4f, 0.8f));
    g.DrawRectangle(c.GetSolidBrush(Rgb(255, 127, 80)), 0, 0, 100, 60);
    g.DrawRectangle(PenOf(c, Rgb(139, 69, 19), 3), 0, 0, 100, 60);
    g.PopTransform();
    g.PopTransform();

    g.PushTransform(new Matrix3x2(1, 0, -0.5f, 1, 340, 200));
    g.DrawRectangle(c.GetSolidBrush(Rgb(138, 43, 226, 220)), 0, 0, 90, 70);
    g.PopTransform();
}

static void Compositing(Canvas g, Win2dRenderContext c)
{
    // Blend modes act on what is already below them.
    g.DrawRectangle(c.GetSolidBrush(Rgb(255, 214, 102)), 20, 20, 160, 120);
    g.DrawRectangle(c.GetSolidBrush(Rgb(102, 204, 255)), 100, 60, 160, 120);
    g.PushBlendMode(PaintBlendMode.Multiply);
    var disc = g.GetGraphicsPath();
    disc.AddCircle(150, 100, 55);
    g.DrawPath(c.GetSolidBrush(Rgb(255, 80, 120)), disc);
    g.PopBlendMode();

    // A layer is composited as one piece: its overlapping shapes do not show through each other at 50% opacity.
    using (var layer = g.BeginLayer(new LayerOptions(Opacity: 0.5)))
    {
        var canvas = layer!.Canvas;
        canvas.DrawRectangle(c.GetSolidBrush(Rgb(220, 20, 60)), 290, 20, 100, 100);
        canvas.DrawRectangle(c.GetSolidBrush(Rgb(30, 144, 255)), 340, 70, 100, 100);
    }

    // Layer effects: a blur, and a drop shadow.
    using (var blurred = g.BeginLayer(new LayerOptions(Bounds: new Rect(20, 190, 180, 100), Effects: new LayerEffect[] { new BlurEffect(4) })))
    {
        blurred!.Canvas.DrawRectangle(c.GetSolidBrush(Rgb(34, 139, 34)), 40, 205, 140, 70);
    }

    using (var shadowed = g.BeginLayer(new LayerOptions(Bounds: new Rect(230, 190, 240, 100), Effects: new LayerEffect[] { new DropShadowEffect(6, 6, 4, 4, Rgb(0, 0, 0, 140)) })))
    {
        var canvas = shadowed!.Canvas;
        var star = canvas.GetGraphicsPath();
        star.AddStar(330, 235, 38, 17, 5, -Math.PI / 2);
        canvas.DrawPath(c.GetSolidBrush(Rgb(255, 193, 7)), star);
    }
}

static void BlendInsideAClip(Canvas g, Win2dRenderContext c)
{
    // Both columns draw the same multiply-blended circle over the same backdrop; the right one is inside a clip. Each shape carries its own
    // clip, so the blend still reaches the backdrop behind the clip (a clip group in SVG would isolate it and leave the circle plain blue).
    foreach (var x in new[] { 20.0, 250.0 })
        g.DrawRectangle(c.GetSolidBrush(Rgb(255, 214, 102)), x, 20, 210, 120);

    for (var i = 0; i < 2; i++)
    {
        var x = 20.0 + i * 230;
        if (i == 1)
            g.PushClip(new Rect(250, 30, 190, 100));

        g.PushBlendMode(PaintBlendMode.Multiply);
        var circle = g.GetGraphicsPath();
        circle.AddCircle(x + 105, 80, 55);
        g.DrawPath(c.GetSolidBrush(Rgb(102, 153, 255)), circle);
        g.PopBlendMode();

        if (i == 1)
            g.PopClip();
    }
}

static void PatternsAndImages(Canvas g, Win2dRenderContext c)
{
    // A hatch brush, and a tile brush whose tile is vector content drawn on a tile canvas (stays vector in the SVG).
    g.DrawRectangle(new HatchBrush(HatchStyle.DiagonalCross, Rgb(70, 70, 70), Rgb(250, 250, 240), 10, 1), 20, 20, 200, 120);

    if (g.CreateTile(24, 24) is { } tile)
    {
        var t = tile.Graphics;
        t.DrawRectangle(c.GetSolidBrush(Rgb(255, 245, 200)), 0, 0, 24, 24);
        var dot = t.GetGraphicsPath();
        dot.AddCircle(12, 12, 7);
        t.DrawPath(c.GetSolidBrush(Rgb(210, 60, 60)), dot);
        t.Dispose();

        // The tile grid is rotated by the brush's transform.
        g.DrawRectangle(new TileBrush(tile.Image, 24, 24, Matrix3x2.CreateRotation(0.35f)), 250, 20, 210, 120);
    }

    // A bitmap made with the raster canvas, embedded as a PNG, drawn whole, scaled, and cropped.
    var raster = new RasterRenderContext();
    using var bitmapCanvas = raster.CreateCanvas(64, 64);
    bitmapCanvas.DrawRectangle(raster.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(64, 64), [(Rgb(255, 0, 128), 0), (Rgb(0, 128, 255), 1)]), 0, 0, 64, 64);
    bitmapCanvas.DrawRectangle(raster.GetSolidBrush(Rgb(255, 255, 255, 220)), 16, 16, 32, 32);
    using var png = new MemoryStream();
    bitmapCanvas.Save(png, "png", new PeachImage.Formats.Png.PngEncoderOptions());
    png.Position = 0;
    var image = c.ImageFromStream(png);

    g.DrawImage(image, new Rect(20, 170, 100, 100));
    g.DrawImage(image, new Rect(140, 170, 100, 100), new Rect(0, 0, 32, 32));
    image.Interpolate = false;
    g.DrawImage(image, new Rect(260, 170, 100, 100), new Rect(16, 16, 16, 16), ImageSampling.Pixelated);
}

static void Text(Canvas g, Win2dRenderContext c)
{
    var title = c.GetFont("Source Sans 3", 40, PaintFontStyle.Regular)!;
    var bold = c.GetFont("Source Sans 3", 28, PaintFontStyle.Bold)!;
    var italic = c.GetFont("Source Sans 3", 28, PaintFontStyle.Italic)!;
    var small = c.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;

    g.DrawString("Vector text", title, Rgb(25, 25, 112), new PaintPoint(20, 20), g.MeasureString("Vector text", title));
    g.DrawString("Synthetic bold", bold, Rgb(139, 0, 0), new PaintPoint(20, 80), g.MeasureString("Synthetic bold", bold));
    g.DrawString("Synthetic italic", italic, Rgb(0, 100, 0), new PaintPoint(20, 120), g.MeasureString("Synthetic italic", italic));
    g.DrawString("Every glyph is an exact outline, kerning and all: AVATAR To Wa.", small, Rgb(40, 40, 40), new PaintPoint(20, 170),
        g.MeasureString("Every glyph is an exact outline, kerning and all: AVATAR To Wa.", small));

    // Text on a gradient: the outline path of a run is a path like any other.
    var big = c.GetFont("Source Sans 3", 64, PaintFontStyle.Bold)!;
    var outline = g.GetTextOutline("PEACH", big, new PaintPoint(20, 270));
    if (outline is not null)
    {
        g.DrawPath(c.GetLinearGradientBrush(new PaintPoint(20, 220), new PaintPoint(240, 270), [(Rgb(255, 154, 0), 0), (Rgb(255, 0, 110), 1)]), outline);
        g.DrawPath(PenOf(c, Rgb(120, 40, 20), 1.5), outline);
    }
}

static void Links(Canvas g, Win2dRenderContext c)
{
    var font = c.GetFont("Source Sans 3", 28, PaintFontStyle.Regular)!;
    g.DrawString("PeachPDF on the web", font, Rgb(20, 80, 200), new PaintPoint(20, 20), g.MeasureString("PeachPDF on the web", font));
    var size = g.MeasureString("PeachPDF on the web", font);
    g.AddLink(new Rect(20, 20, size.Width, size.Height), "https://peachpdf.net/");

    // A host would now call composer.Links.TryActivateAsync(x, y) when the user clicks; an https link opens in the default browser.
}

static Pen PenOf(Win2dRenderContext c, PaintColor color, double width)
{
    var pen = c.GetPen(color);
    pen.Width = width;
    return pen;
}
