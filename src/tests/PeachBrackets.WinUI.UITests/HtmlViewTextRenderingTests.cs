using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using PeachDrawing.Win2D;

namespace PeachBrackets.WinUI.UITests;

/// <summary>The text an <see cref="HtmlView"/> shows, read back from what the control actually displays.</summary>
public class HtmlViewTextRenderingTests
{
    private const string Page = "<body style='margin:0;font:16px Segoe UI'><p style='margin:8px'>Hamburgefonstiv, the quick brown fox jumps over the lazy dog.</p></body>";

    private sealed record Capture(int Fringed, int Ink, int Pixels);

    // Shows the view with a text rendering, waits until it has painted, and counts the pixels of what is on screen that are coloured (the
    // stripes of LCD text) and that are not the white page.
    private static async Task<Capture> CaptureAsync(Win2dTextRendering rendering)
    {
        return await UiThread.RunAsync(async window =>
        {
            var view = new HtmlView { Html = Page, TextRendering = rendering, Width = 600, Height = 120 };
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            view.DocumentLoaded += (_, _) => loaded.TrySetResult();
            view.DocumentFailed += (_, e) => loaded.TrySetException(e.Exception);
            window.Content = view;
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await Task.Delay(1500);

            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(view);
            Windows.Storage.Streams.IBuffer buffer = await bitmap.GetPixelsAsync();
            var pixels = buffer.ToArray();

            int fringed = 0, ink = 0;
            for (var i = 0; i < pixels.Length; i += 4)
            {
                var (b, g, r) = (pixels[i], pixels[i + 1], pixels[i + 2]);
                if (b != g || g != r)
                    fringed++;

                if (g < 200)
                    ink++;
            }

            return new Capture(fringed, ink, pixels.Length / 4);
        });
    }

    [Fact]
    public async Task Lcd_text_in_the_view_has_colour_fringes_and_grayscale_text_has_none()
    {
        var lcd = await CaptureAsync(Win2dTextRendering.Lcd);
        var grayscale = await CaptureAsync(Win2dTextRendering.Grayscale);

        Assert.True(lcd.Ink > 200, $"text is drawn: {lcd}");
        Assert.True(lcd.Fringed > 50, $"LCD stripes reach the screen: {lcd}");
        Assert.True(grayscale.Ink > 200, $"text is drawn: {grayscale}");
        Assert.Equal(0, grayscale.Fringed);
    }

    [Fact]
    public async Task The_view_draws_the_same_amount_of_text_in_every_mode()
    {
        var outline = await CaptureAsync(Win2dTextRendering.Outline);
        var auto = await CaptureAsync(Win2dTextRendering.Auto);
        var lcd = await CaptureAsync(Win2dTextRendering.Lcd);

        Assert.InRange(auto.Ink, outline.Ink * 0.8, outline.Ink * 1.4);
        Assert.InRange(lcd.Ink, outline.Ink * 0.8, outline.Ink * 1.5);
        Assert.Equal(0, outline.Fringed);
    }
}
