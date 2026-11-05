using CoreGraphics;
using Foundation;
using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;
using PeachDrawing.CoreGraphics.Examples;
using UIKit;

namespace PeachDrawing.CoreGraphics.IOSExample;

public static class Program
{
    public static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
public class AppDelegate : UIApplicationDelegate
{
    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions) => true;
}

/// <summary>iOS 26 and later require the scene lifecycle: the window is made when the scene connects.</summary>
[Register("SceneDelegate")]
public class SceneDelegate : UIResponder, IUIWindowSceneDelegate
{
    [Export("window")]
    public UIWindow? Window { get; set; }

    [Export("scene:willConnectToSession:options:")]
    public void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        if (scene is not UIWindowScene windowScene)
            return;

        Window = new UIWindow(windowScene) { RootViewController = new UIViewController { View = new SceneView() } };
        Window.MakeKeyAndVisible();
    }
}

/// <summary>Draws the scene with the CGContext UIKit gives it (top-left origin), and checks the same scene in a bitmap.</summary>
public sealed class SceneView : UIView
{
    private readonly CoreGraphicsRenderContext _context;
    private bool _checked;

    public SceneView()
    {
        BackgroundColor = UIColor.White;
        _context = new CoreGraphicsRenderContext(imageCodec: CoreGraphicsImageCodec.Auto);
        var path = NSBundle.MainBundle.PathForResource("SourceSans3-Regular", "ttf");
        using var font = File.OpenRead(path!);
        _context.AddFont(font, "Source Sans 3").AsTask().GetAwaiter().GetResult();
        Log($"context: gpu={_context.EffectsUseGpu} fonts ok");
    }

    private static void Log(string message) => Console.WriteLine("PEACH_SMOKE " + message);

    public override void Draw(CGRect rect)
    {
        var cg = UIGraphics.GetCurrentContext()!;
        var scale = (double)UIScreen.MainScreen.Scale;
        _context.PixelsPerPoint = 1;
        using (var canvas = CoreGraphicsCanvas.FromContext(_context, cg, new Rect(0, 0, Bounds.Width, Bounds.Height), originAtBottomLeft: false))
            DrawScene(canvas, Bounds.Width);

        if (!_checked)
        {
            _checked = true;
            SelfCheck();
        }
    }

    private void DrawScene(CoreGraphicsCanvas canvas, double width)
    {
        var red = PaintColor.FromArgb(255, 220, 70, 50);
        var blue = PaintColor.FromArgb(255, 40, 90, 200);
        using var linear = canvas.GetLinearGradientBrush(new PaintPoint(0, 80), new PaintPoint(width, 80), [(red, 0), (blue, 1)]);
        canvas.DrawRectangle(linear, 10, 80, width - 20, 60);

        using var radial = canvas.GetRadialGradientBrush(new PaintPoint(90, 220), 60, 60, [(PaintColor.White, 0), (blue, 1)]);
        canvas.DrawRectangle(radial, 30, 160, 120, 120);

        using var conic = canvas.GetConicGradientBrush(new PaintPoint(250, 220), 60, [red, blue, red], [0, Math.PI, 2 * Math.PI]);
        canvas.DrawRectangle(conic, 190, 160, 120, 120);

        var heading = _context.GetFont("Source Sans 3", 28, PaintFontStyle.Bold)!;
        canvas.DrawString("CoreGraphics on iOS", heading, PaintColor.Black, new PaintPoint(10, 40), canvas.MeasureString("CoreGraphics on iOS", heading));

        var system = _context.GetFont(_context.GetFont("system-ui", 18, PaintFontStyle.Regular) is null ? "Source Sans 3" : "System Font", 18, PaintFontStyle.Regular)!;
        canvas.DrawString("Top is up, text is upright", system, PaintColor.Black, new PaintPoint(10, 300), canvas.MeasureString("Top is up, text is upright", system));

        using (var layer = canvas.BeginLayer(new LayerOptions(Opacity: 0.8, Bounds: new Rect(0, 340, width, 150),
                   Effects: [new DropShadowEffect(4, 4, 3, 3, PaintColor.FromArgb(120, 0, 0, 0))])))
        {
            using var peach = canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 160, 100));
            layer!.Canvas.DrawRectangle(peach, 40, 360, 120, 80);
            layer.Canvas.DrawRectangle(peach, 100, 390, 120, 80);
        }
    }

    /// <summary>The same scene on a bitmap, checked by pixel: orientation, text ink, layer opacity, effects, images.</summary>
    private void SelfCheck()
    {
        var failures = 0;
        void Check(string name, bool ok)
        {
            Log($"{(ok ? "PASS" : "FAIL")} {name}");
            if (!ok) failures++;
        }

        using var composer = new CoreGraphicsComposer(_context, 320, 500, 320, 500, new CoreGraphicsComposerOptions { Background = PaintColor.White });
        DrawScene(composer.Canvas, 320);
        var px = composer.ToPixelBuffer();
        (int R, int G, int B, int A) At(int x, int y)
        {
            var s = px.PremultipliedRgba.Span;
            var i = (y * px.Width + x) * 4;
            return (s[i], s[i + 1], s[i + 2], s[i + 3]);
        }

        Check("linear gradient: red left, blue right", At(14, 110).R > At(14, 110).B && At(305, 110).B > At(305, 110).R);
        Check("radial gradient: white centre", At(90, 220).R > 230 && At(90, 220).G > 230);
        Check("conic gradient drawn", At(280, 220).A == 255 && At(250, 190) != At(250, 250));
        Check("text has ink near its top-left", Enumerable.Range(10, 250).Any(x => Enumerable.Range(40, 34).Any(y => At(x, y).R < 100)));
        Check("layer content opaque-ish", At(70, 380).A > 150 && At(70, 380).R > 200);
        Check("layer shadow below-right", At(170, 462) is { A: > 20 } && At(300, 450).A == 255 && At(300, 450).R > 240);

        // Images: PNG through each codec, drawn upright.
        foreach (var codec in new[] { CoreGraphicsImageCodec.PeachImage, CoreGraphicsImageCodec.Native, CoreGraphicsImageCodec.Auto })
        {
            using var context = new CoreGraphicsRenderContext(imageCodec: codec);
            using var c2 = new CoreGraphicsComposer(context, 20, 8, 20, 8);
            using var source = new CoreGraphicsComposer(context, 8, 4, 8, 4);
            source.Canvas.DrawRectangle(source.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 4, 4);
            source.Canvas.DrawRectangle(source.Canvas.GetSolidBrush(PaintColor.FromArgb(255, 0, 0, 255)), 4, 0, 4, 4);
            using var png = new MemoryStream();
            source.Save(png);
            using var image = context.ImageFromStream(new MemoryStream(png.ToArray()));
            c2.Canvas.DrawImage(image, new Rect(0, 0, 20, 8));
            var p2 = c2.ToPixelBuffer().PremultipliedRgba.Span;
            Check($"image via {codec}: red left, blue right", p2[(4 * 20 + 3) * 4] > 200 && p2[(4 * 20 + 17) * 4 + 2] > 200);
        }

        // How heavy CoreText's text is next to the shared engine's, for the same font file (1.0 = the same ink).
        foreach (var size in new[] { 13.0, 16.0, 24.0 })
        {
            double Ink(CoreGraphicsTextEngine engine)
            {
                using var context = new CoreGraphicsRenderContext(engine);
                using var stream = File.OpenRead(NSBundle.MainBundle.PathForResource("SourceSans3-Regular", "ttf")!);
                context.AddFont(stream, "Source Sans 3").AsTask().GetAwaiter().GetResult();
                using var text = new CoreGraphicsComposer(context, 220, 40, 220, 40);
                var font = context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
                text.Canvas.DrawString("Hamburgefonstiv 0123", font, PaintColor.Black, new PaintPoint(4, 6), text.Canvas.MeasureString("Hamburgefonstiv 0123", font));
                var span = text.ToPixelBuffer().PremultipliedRgba.Span;
                double sum = 0;
                for (var i = 3; i < span.Length; i += 4) sum += span[i];
                return sum;
            }

            Log($"INFO text ink CoreText/PeachDrawing.Text at {size}px 1x = {Ink(CoreGraphicsTextEngine.Native) / Ink(CoreGraphicsTextEngine.PeachDrawing):F3}");
        }

        // CoreGraphics places glyphs on a size-dependent sub-pixel grid; the shared engine must change its drawing at the same positions.
        foreach (var size in new[] { 9.0, 14.0, 20.0, 40.0 })
        {
            List<int> Boundaries(CoreGraphicsTextEngine engine)
            {
                var changes = new List<int>();
                string? previous = null;
                using var context = new CoreGraphicsRenderContext(engine);
                using var stream = File.OpenRead(NSBundle.MainBundle.PathForResource("SourceSans3-Regular", "ttf")!);
                context.AddFont(stream, "Source Sans 3").AsTask().GetAwaiter().GetResult();
                for (var i = 0; i < 24; i++)
                {
                    using var c = new CoreGraphicsComposer(context, 100, 40, 100, 40);
                    var font = context.GetFont("Source Sans 3", size, PaintFontStyle.Regular)!;
                    c.Canvas.DrawString("H", font, PaintColor.Black, new PaintPoint(5 + i / 24.0, 5), c.Canvas.MeasureString("H", font));
                    var key = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(c.ToPixelBuffer().PremultipliedRgba.ToArray()));
                    if (key != previous)
                        changes.Add(i);

                    previous = key;
                }

                return changes;
            }

            var peach = Boundaries(CoreGraphicsTextEngine.PeachDrawing);
            var native = Boundaries(CoreGraphicsTextEngine.Native);
            Check($"glyph grid at {size}px: shared engine [{string.Join(",", peach)}] vs CoreGraphics [{string.Join(",", native)}]", peach.SequenceEqual(native));
        }

        // The Lorem Ipsum paragraph through both engines, written to the app's Documents folder for inspection.
        var documents = NSSearchPath.GetDirectories(NSSearchPathDirectory.DocumentDirectory, NSSearchPathDomain.User)[0];
        LoremScene.RenderAllAsync(documents, NSBundle.MainBundle.PathForResource("SourceSans3-Regular", "ttf")!).GetAwaiter().GetResult();
        Log("LOREM " + documents);

        Log(failures == 0 ? "RESULT ALL PASSED" : $"RESULT {failures} FAILED");
    }
}
