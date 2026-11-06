using System.Runtime.InteropServices;
using AppKit;
using CoreAnimation;
using CoreGraphics;
using Foundation;

namespace PeachBrackets.Apple.AppTests;

/// <summary>Runs work on the AppKit main thread and waits for it.</summary>
internal static class MainThread
{
    public static Task<T> Run<T>(Func<T> work)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        NSRunLoop.Main.BeginInvokeOnMainThread(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        });
        return completion.Task;
    }

    public static Task Run(Action work) => Run(() =>
    {
        work();
        return true;
    });
}

/// <summary>An <see cref="HtmlView"/> in an offscreen window with a document loaded, and the means to drive it as a person would and to look at what it painted.</summary>
internal sealed class ViewHarness
{
    private const double CssPixelsPerPoint = 96.0 / 72.0;
    private int _loads;

    private ViewHarness(NSWindow window, HtmlView view)
    {
        Window = window;
        View = view;
        view.DocumentLoaded += (_, _) => Interlocked.Increment(ref _loads);
    }

    public NSWindow Window { get; }

    public HtmlView View { get; }

    public int Loads => _loads;

    public static async Task<ViewHarness> LoadAsync(string html, double width = 800, double height = 600, Action<HtmlView>? configure = null)
    {
        var harness = await MainThread.Run(() =>
        {
            var window = new NSWindow(new CGRect(100, 100, width, height), NSWindowStyle.Titled | NSWindowStyle.Resizable, NSBackingStore.Buffered, false);
            var view = new HtmlView { Frame = new CGRect(0, 0, width, height), AutoresizingMask = NSViewResizingMask.WidthSizable | NSViewResizingMask.HeightSizable };
            window.ContentView = view;
            var created = new ViewHarness(window, view);
            configure?.Invoke(view);
            view.Html = html;
            return created;
        });

        await harness.WaitForLoadsAsync(1);
        return harness;
    }

    // The document has loaded that many times and the first paint has settled.
    public async Task WaitForLoadsAsync(int loads)
    {
        await Eventually(() => _loads >= loads, 30);
        await Task.Delay(400);
    }

    public static async Task Eventually(Func<bool> condition, double seconds = 5)
    {
        var until = DateTime.UtcNow.AddSeconds(seconds);
        while (DateTime.UtcNow < until)
        {
            if (await MainThread.Run(condition))
                return;

            await Task.Delay(50);
        }

        Assert.True(await MainThread.Run(condition), "The condition did not become true in " + seconds + " s.");
    }

    // ---- where things are ----

    // The middle of an element, or a fraction across it, in window coordinates.
    private CGPoint WindowPoint(string id, double fraction = 0.5)
    {
        var bounds = View.Document!.GetElementBounds(id) ?? throw new InvalidOperationException("No element " + id);
        var content = new CGPoint((bounds.X + (bounds.Width * fraction)) * CssPixelsPerPoint, (bounds.Y + (bounds.Height / 2)) * CssPixelsPerPoint);
        return View.DocumentView!.ConvertPointToView(content, null);
    }

    // ---- the mouse and keyboard, as the window would receive them ----

    private NSEvent Mouse(NSEventType type, CGPoint at, int clicks = 1) => NSEvent.MouseEvent(type, at, 0, 0, Window.WindowNumber, null, 0, clicks, 1)!;

    // The events go straight to the page view, as the window would hand them over: an offscreen window delivers none.
    private NSView Page => View.DocumentView!;

    public Task ClickAsync(string id) => MainThread.Run(() =>
    {
        var at = WindowPoint(id);
        Window.MakeFirstResponder(Page);
        Page.MouseDown(Mouse(NSEventType.LeftMouseDown, at));
        Page.MouseUp(Mouse(NSEventType.LeftMouseUp, at));
    });

    public Task MoveMouseToAsync(string id) => MainThread.Run(() => Page.MouseMoved(Mouse(NSEventType.MouseMoved, WindowPoint(id))));

    public Task DragAsync(string id, double from, double to) => MainThread.Run(() =>
    {
        Window.MakeFirstResponder(Page);
        Page.MouseDown(Mouse(NSEventType.LeftMouseDown, WindowPoint(id, from)));
        for (var step = 1; step <= 4; step++)
            Page.MouseDragged(Mouse(NSEventType.LeftMouseDragged, WindowPoint(id, from + ((to - from) * step / 4))));

        Page.MouseUp(Mouse(NSEventType.LeftMouseUp, WindowPoint(id, to)));
    });

    public async Task TypeAsync(string text)
    {
        foreach (var character in text)
        {
            await MainThread.Run(() =>
            {
                var s = character.ToString();
                Page.KeyDown(NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, 0, 0, Window.WindowNumber, null, s, s, false, 0)!);
                Page.KeyUp(NSEvent.KeyEvent(NSEventType.KeyUp, CGPoint.Empty, 0, 0, Window.WindowNumber, null, s, s, false, 0)!);
            });
            await Task.Delay(20);
        }
    }

    // ---- what was painted ----

    /// <summary>The colour at a point of what the view paints over its tiles (what the viewport holds in place), counted from the top-left of what is in view. Main thread only.</summary>
    public (byte R, byte G, byte B)? OverlayPixel(double x, double y)
    {
        // The overlay is the last layer of the page view's own: it sits over the tiles, at the scroll offset, as big as the view.
        var overlay = View.DocumentView!.Layer!.Sublayers?.LastOrDefault();
        if (overlay?.Contents is not CGImage picture)
            return null;

        var scale = picture.Width / overlay.Frame.Width;
        var pixels = Read(picture, (int)(x * scale), (int)(y * scale), 1, 1);
        return pixels.Length == 4 ? (pixels[0], pixels[1], pixels[2]) : null;
    }

    // The painted tile layers: each is a CALayer whose contents are the CGImage of its part of the page, at the frame (in CSS pixels) it sits at.
    private IEnumerable<CALayer> TileLayers()
    {
        static IEnumerable<CALayer> Walk(CALayer layer)
        {
            foreach (var child in layer.Sublayers ?? [])
            {
                if (child.Contents is not null)
                    yield return child;

                foreach (var nested in Walk(child))
                    yield return nested;
            }
        }

        return Walk(View.DocumentView!.Layer!);
    }

    /// <summary>The colour painted at a point of the content (CSS pixels from its top-left), or null where no tile has been painted. Main thread only.</summary>
    public (byte R, byte G, byte B)? PixelAt(double x, double y)
    {
        foreach (var tile in TileLayers())
        {
            if (!tile.Frame.Contains(new CGPoint(x, y)) || tile.Contents is not CGImage image)
                continue;

            var scale = image.Width / tile.Frame.Width;
            return Read(image, (int)((x - tile.Frame.X) * scale), (int)((y - tile.Frame.Y) * scale), 1, 1) is { Length: 4 } p ? (p[0], p[1], p[2]) : null;
        }

        return null;
    }

    public Task<(byte R, byte G, byte B)?> PixelAtAsync(double x, double y) => MainThread.Run(() => PixelAt(x, y));

    /// <summary>Whether any painted pixel of a rectangle of the content (CSS pixels) is darker than a threshold: ink where there was paper.</summary>
    public Task<bool> HasInkAsync(double x, double y, double width, double height, int threshold = 100) => MainThread.Run(() =>
    {
        foreach (var tile in TileLayers())
        {
            if (!tile.Frame.Contains(new CGPoint(x, y)) || tile.Contents is not CGImage image)
                continue;

            var scale = image.Width / tile.Frame.Width;
            var left = (int)((x - tile.Frame.X) * scale);
            var top = (int)((y - tile.Frame.Y) * scale);
            var w = Math.Min((int)(width * scale), (int)image.Width - left);
            var h = Math.Min((int)(height * scale), (int)image.Height - top);
            var pixels = Read(image, left, top, w, h);
            for (var i = 0; i + 3 < pixels.Length; i += 4)
            {
                if (pixels[i] < threshold && pixels[i + 1] < threshold && pixels[i + 2] < threshold)
                    return true;
            }
        }

        return false;
    });

    // The RGBA bytes of a part of an image, top row first.
    private static byte[] Read(CGImage image, int x, int y, int width, int height)
    {
        using var space = CGColorSpace.CreateSrgb();
        using var context = new CGBitmapContext(IntPtr.Zero, (int)image.Width, (int)image.Height, 8, 0, space, CGImageAlphaInfo.PremultipliedLast);
        context.DrawImage(new CGRect(0, 0, image.Width, image.Height), image);
        var stride = (int)context.BytesPerRow;
        var data = new byte[stride * (int)image.Height];
        Marshal.Copy(context.Data, data, 0, data.Length);
        var result = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Array.Copy(data, ((y + row) * stride) + (x * 4), result, row * width * 4, width * 4);

        return result;
    }
}
