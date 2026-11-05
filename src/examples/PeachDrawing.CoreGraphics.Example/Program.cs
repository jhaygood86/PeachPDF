using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;

// Draws a few scenes with PeachDrawing.CoreGraphics and writes each as a PNG into the folder given as the first argument (default: next to the executable).
var output = args.Length > 0 ? args[0] : AppContext.BaseDirectory;
Directory.CreateDirectory(output);

using var context = new CoreGraphicsRenderContext();
await using (var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
    await context.AddFont(font, "Source Sans 3");

Render("shapes", 320, 200, canvas =>
{
    var red = PaintColor.FromArgb(255, 220, 70, 50);
    var blue = PaintColor.FromArgb(255, 40, 90, 200);

    using var linear = canvas.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(320, 0), [(red, 0), (blue, 1)]);
    canvas.DrawRectangle(linear, 10, 10, 300, 60);

    using var radial = canvas.GetRadialGradientBrush(new PaintPoint(80, 140), 50, 50, [(PaintColor.White, 0), (blue, 1)]);
    canvas.DrawRectangle(radial, 30, 90, 100, 100);

    using var conic = canvas.GetConicGradientBrush(new PaintPoint(220, 140), 50, [red, blue, red], [0, Math.PI, 2 * Math.PI]);
    canvas.DrawRectangle(conic, 170, 90, 100, 100);

    var pen = canvas.GetPen(PaintColor.Black);
    pen.Width = 3;
    pen.DashStyle = DashStyle.Dash;
    canvas.DrawRectangle(pen, 25, 85, 250, 110);
});

Render("text-and-layers", 320, 200, canvas =>
{
    var heading = context.GetFont("Source Sans 3", 28, PaintFontStyle.Bold)!;
    canvas.DrawString("CoreGraphics", heading, PaintColor.Black, new PaintPoint(10, 10), canvas.MeasureString("CoreGraphics", heading));

    // A group faded as one piece, with a soft shadow: overlapping shapes inside it do not show through each other. Core Image runs the shadow.
    using (var layer = canvas.BeginLayer(new LayerOptions(Opacity: 0.8, Bounds: new Rect(0, 60, 320, 140),
               Effects: [new DropShadowEffect(4, 4, 3, 3, PaintColor.FromArgb(120, 0, 0, 0))])))
    {
        using var peach = canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 160, 100));
        layer!.Canvas.DrawRectangle(peach, 40, 80, 120, 80);
        layer.Canvas.DrawRectangle(peach, 100, 100, 120, 80);
    }
});

// The same line at one pixel to a point: unhinted (Apple's own look, right for Retina) and lightly hinted (vertical fitting only, crisper at 1x). Enlarged so the difference shows.
foreach (var (label, hinting) in new[] { ("hinting-none", TextHinting.None), ("hinting-standard", TextHinting.Standard) })
{
    using var plain = new CoreGraphicsRenderContext { TextHinting = hinting };
    await using (var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
        await plain.AddFont(font, "Source Sans 3");
    var face = plain.GetFont("Source Sans 3", 13, PaintFontStyle.Regular)!;
    using var composer = new CoreGraphicsComposer(plain, 150, 24, 150, 24, new CoreGraphicsComposerOptions { Background = PaintColor.White });
    composer.Canvas.DrawString("Hamburgefonstiv 0123", face, PaintColor.Black, new PaintPoint(4, 4), composer.Canvas.MeasureString("Hamburgefonstiv 0123", face));
    using var file = File.Create(Path.Combine(output, label + ".png"));
    composer.Save(file);
    Console.WriteLine(label);
}

await PeachDrawing.CoreGraphics.Examples.LoremScene.RenderAllAsync(output, Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));

void Render(string name, double width, double height, Action<CoreGraphicsCanvas> draw)
{
    // The package makes no window or view: the composer here owns a bitmap, and an app would pass the CGContext of its view to
    // CoreGraphicsCanvas.FromContext instead.
    using var composer = new CoreGraphicsComposer(context, width, height, (int)(width * 2), (int)(height * 2), new CoreGraphicsComposerOptions { Background = PaintColor.White });
    draw(composer.Canvas);

    var path = Path.Combine(output, name + ".png");
    using var file = File.Create(path);
    composer.Save(file);
    Console.WriteLine(path);
}
