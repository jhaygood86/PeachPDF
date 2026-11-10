using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using PeachDrawing.Core;
using PeachImage.Formats.Png;

namespace PeachDrawing.Benchmarks;

/// <summary>
/// What a page costs the rasterizer, by kind of fill: a solid page-sized rectangle, a gradient, thousands of small shapes (what glyph runs and
/// borders are made of), a clipped fill, and an image drawn at its own size and shrunk. Each invocation paints onto a canvas made in setup and
/// cleared nothing in between, so the figure is the drawing alone; allocations are reported because the scanline path allocates per fill.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0)]
public class FillBenchmarks
{
    private const int Width = 794;
    private const int Height = 1123;

    private RasterRenderContext _context = null!;
    private RasterCanvas _canvas = null!;
    private Brush _solid = null!;
    private Brush _translucent = null!;
    private Brush _linear = null!;
    private Brush _radial = null!;
    private GraphicsPath _circle = null!;
    private GraphicsPath[] _smallCircles = [];
    private Image _photo = null!;

    [GlobalSetup]
    public void Setup()
    {
        _context = new RasterRenderContext();
        _canvas = _context.CreateCanvas(Width, Height);
        _solid = _context.GetSolidBrush(PaintColor.FromArgb(255, 30, 144, 255));
        _translucent = _context.GetSolidBrush(PaintColor.FromArgb(128, 220, 60, 40));
        _linear = _context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(Width, Height),
            [(Color(255, 0, 0), 0d), (Color(255, 220, 0), 0.4), (Color(0, 160, 80), 0.7), (Color(0, 60, 220), 1d)]);
        _radial = _context.GetRadialGradientBrush(new PaintPoint(Width / 2d, Height / 2d), Width / 2d, Height / 2d,
            [(Color(255, 255, 255), 0d), (Color(40, 40, 160), 1d)]);

        _circle = Circle(Width / 2d, Height / 2d, 300);
        _smallCircles = new GraphicsPath[2000];
        for (int i = 0; i < _smallCircles.Length; i++)
            _smallCircles[i] = Circle(10 + i % 70 * 11, 10 + i / 70 * 11, 4);

        // A photo-sized source to draw whole and shrunk: a gradient page rendered once and round-tripped through PNG, the way a decoded image arrives.
        using var source = _context.CreateCanvas(1600, 1200);
        source.DrawRectangle(_context.GetLinearGradientBrush(new PaintPoint(0, 0), new PaintPoint(1600, 1200),
            [(Color(250, 200, 20), 0d), (Color(20, 120, 250), 1d)]), 0, 0, 1600, 1200);
        for (int i = 0; i < 300; i++)
            source.DrawPath(i % 2 == 0 ? _solid : _translucent, Circle(i * 5.3 % 1600, i * 7.1 % 1200, 40 + i % 30));
        using var png = new MemoryStream();
        source.Save(png, "png", new PngEncoderOptions());
        png.Position = 0;
        _photo = _context.ImageFromStream(png);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _canvas.Dispose();
    }

    private static PaintColor Color(int r, int g, int b) => PaintColor.FromArgb(255, r, g, b);

    private GraphicsPath Circle(double cx, double cy, double r)
    {
        const double k = 0.5522847498;
        var path = _canvas.GetGraphicsPath();
        path.Start(cx + r, cy);
        path.AddBezierTo(cx + r, cy + k * r, cx + k * r, cy + r, cx, cy + r);
        path.AddBezierTo(cx - k * r, cy + r, cx - r, cy + k * r, cx - r, cy);
        path.AddBezierTo(cx - r, cy - k * r, cx - k * r, cy - r, cx, cy - r);
        path.AddBezierTo(cx + k * r, cy - r, cx + r, cy - k * r, cx + r, cy);
        path.CloseFigure();
        return path;
    }

    /// <summary>A page-sized opaque rectangle (a canvas or body background).</summary>
    [Benchmark]
    public void SolidRectangle() => _canvas.DrawRectangle(_solid, 0, 0, Width, Height);

    /// <summary>A page-sized translucent rectangle (the blend path with real coverage everywhere).</summary>
    [Benchmark]
    public void TranslucentRectangle() => _canvas.DrawRectangle(_translucent, 0, 0, Width, Height);

    /// <summary>A page-sized four-stop linear gradient.</summary>
    [Benchmark]
    public void LinearGradient() => _canvas.DrawRectangle(_linear, 0, 0, Width, Height);

    /// <summary>A page-sized two-stop radial gradient.</summary>
    [Benchmark]
    public void RadialGradient() => _canvas.DrawRectangle(_radial, 0, 0, Width, Height);

    /// <summary>One large curved path: edge building and the interior of a big antialiased fill.</summary>
    [Benchmark]
    public void LargePath() => _canvas.DrawPath(_solid, _circle);

    /// <summary>Two thousand tiny paths, as a page of glyphs or bullets: the per-fill setup cost.</summary>
    [Benchmark]
    public void ManySmallPaths()
    {
        foreach (var path in _smallCircles)
            _canvas.DrawPath(_solid, path);
    }

    /// <summary>A gradient page filled through a large curved clip (rounded-corner overflow, <c>clip-path</c>).</summary>
    [Benchmark]
    public void ClippedGradient()
    {
        _canvas.PushClip(_circle);
        _canvas.DrawRectangle(_linear, 0, 0, Width, Height);
        _canvas.PopClip();
    }

    /// <summary>An image drawn at about its own size.</summary>
    [Benchmark]
    public void ImageAtNaturalSize() => _canvas.DrawImage(_photo, new Rect(0, 0, 1600, 1200));

    /// <summary>A 1600x1200 image shrunk to a thumbnail.</summary>
    [Benchmark]
    public void ImageShrunk() => _canvas.DrawImage(_photo, new Rect(0, 0, 320, 240));

    /// <summary>A 1600x1200 image shrunk to fit a page width.</summary>
    [Benchmark]
    public void ImageFitToPage() => _canvas.DrawImage(_photo, new Rect(0, 0, Width, Width * 0.75));
}
