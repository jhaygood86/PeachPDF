using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Tests.TestSupport;
using System.Diagnostics;
using System.Numerics;
using System.Text;
using Windows.Graphics.DirectX;

namespace PeachDrawing.Win2D.Tests;

/// <summary>
/// A spike, not a test of shipped behaviour: can ClearType text be blended over an arbitrary backdrop on the GPU with a pixel shader, correctly and
/// faster than reading the pixels behind each word back? Runs only when <c>PEACH_SPIKE_SHADER</c> names the compiled shader; writes a report to
/// <c>PEACH_SPIKE_OUT</c>. <c>PEACH_SPIKE_DEVICE=warp</c> uses the software device instead of the GPU.
/// </summary>
public class GpuTextSpike
{
    private const int Width = 1200;
    private const int Height = 504;
    private const int Frames = 25;

    private static readonly string[] Words = "The quick brown fox jumps over the lazy dog while seven wizards quietly box jars of pickled quince and ginger".Split(' ');

    private readonly record struct Run(string Text, int X, int Y, PaintColor Color);

    private static List<Run> Layout()
    {
        var runs = new List<Run>();
        var colors = new[] { PaintColor.FromArgb(255, 20, 30, 120), PaintColor.FromArgb(255, 255, 255, 255), PaintColor.FromArgb(255, 160, 20, 20) };
        var word = 0;
        for (var y = 6; y < Height - 22; y += 22)
        {
            for (var x = 8; x < Width - 80; x += 62 + (word * 7 % 23))
                runs.Add(new Run(Words[word++ % Words.Length], x, y, colors[word % colors.Length]));
        }

        return runs;
    }

    private static readonly bool Flat = Environment.GetEnvironmentVariable("PEACH_SPIKE_FLAT") == "1";
    private static readonly Windows.UI.Color FlatColor = Windows.UI.Color.FromArgb(255, 235, 238, 245);

    private static void DrawBackdrop(CanvasDrawingSession session)
    {
        if (Flat)
        {
            session.FillRectangle(0, 0, Width, Height, FlatColor);
            return;
        }

        // A diagonal gradient with a flat band: text lands on all of it.
        using var brush = new Microsoft.Graphics.Canvas.Brushes.CanvasLinearGradientBrush(session, Windows.UI.Color.FromArgb(255, 250, 180, 90), Windows.UI.Color.FromArgb(255, 40, 70, 160))
        {
            StartPoint = new Vector2(0, 0),
            EndPoint = new Vector2(Width, Height),
        };
        session.FillRectangle(0, 0, Width, Height, brush);
    }

    // The three stripes' corrected alphas, one per colour channel (R8G8B8A8: red stripe in R), alpha 255.
    private static byte[] Alphas(Win2dTextMask mask, PaintColor color)
    {
        var table = Win2dTextBlend.CorrectionTable(color, lcd: true);
        var fir = new[] { Win2dTextBlend.FirOuter, Win2dTextBlend.FirInner, 1 - 2 * Win2dTextBlend.FirOuter - 2 * Win2dTextBlend.FirInner, Win2dTextBlend.FirInner, Win2dTextBlend.FirOuter };
        var rowLength = mask.Width * 3;
        var pixels = new byte[mask.Width * mask.Height * 4];
        for (var y = 0; y < mask.Height; y++)
        {
            for (var x = 0; x < mask.Width; x++)
            {
                var o = ((y * mask.Width) + x) * 4;
                for (var stripe = 0; stripe < 3; stripe++)
                {
                    var center = (x * 3) + stripe;
                    var sum = 0.0;
                    for (var k = -2; k <= 2; k++)
                    {
                        var s = center + k;
                        if ((uint)s < (uint)rowLength)
                            sum += fir[k + 2] * mask.Samples[(y * rowLength) + s];
                    }

                    pixels[o + stripe] = table[(int)Math.Min(Math.Round(sum), 255)];
                }

                pixels[o + 3] = 255;
            }
        }

        return pixels;
    }

    private static byte[] InkTimesAlpha(byte[] alphas, PaintColor color)
    {
        var (r, g, b) = color.ToRgb();
        var ink = new[] { r, g, b };
        var pixels = new byte[alphas.Length];
        for (var i = 0; i < alphas.Length; i += 4)
        {
            for (var c = 0; c < 3; c++)
                pixels[i + c] = (byte)((ink[c] * alphas[i + c] + 127) / 255);

            pixels[i + 3] = 255;
        }

        return pixels;
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[sorted.Count / 2];
    }

    private static double P95(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)(sorted.Count * 0.95))];
    }

    private static double Ms(long since) => (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency;

    [Fact]
    public async Task Spike_GpuClearType()
    {
        var shaderPath = Environment.GetEnvironmentVariable("PEACH_SPIKE_SHADER");
        if (string.IsNullOrEmpty(shaderPath))
            return;

        var report = new StringBuilder();
        var outPath = Environment.GetEnvironmentVariable("PEACH_SPIKE_OUT") ?? Path.Combine(Path.GetTempPath(), "gpu-spike.txt");
        try
        {
            var software = Environment.GetEnvironmentVariable("PEACH_SPIKE_DEVICE") == "warp";
            using var device = software ? new CanvasDevice(true) : new CanvasDevice(false);
            using var context = new Win2dRenderContext(device) { TextRendering = Win2dTextRendering.Lcd };
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
                await context.AddFont(stream, "Source Sans 3");
            var font = context.GetFont("Source Sans 3", 16, PaintFontStyle.Regular)!;
            report.AppendLine($"device: {(software ? "WARP (software)" : "default hardware")}, ClearType: {Win2dSystemText.ClearTypeEnabled}");

            var runs = Layout();
            report.AppendLine($"{runs.Count} words, region {Width}x{Height}");

            // ---- the masks both paths start from
            var masks = new List<Win2dTextMask>();
            foreach (var run in runs)
            {
                var mask = context.TextMasks.Get(context, run.Text, font, 0, 1.0, 1.0, 1.0, run.X, run.Y, null, ShapeSettings.Default, 3, null, out _, out _)!.Value;
                masks.Add(mask);
            }

            // ---- path A: what ships today (read the pixels behind each word back, blend on the CPU, upload)
            using var targetA = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
            var timesA = new List<double>();
            for (var frame = 0; frame < Frames; frame++)
            {
                var started = Stopwatch.GetTimestamp();
                using (var session = targetA.CreateDrawingSession())
                {
                    DrawBackdrop(session);
                    using var canvas = Win2dCanvas.FromSession(context, session, new Rect(0, 0, Width, Height));
                    foreach (var run in runs)
                        canvas.DrawString(run.Text, font, run.Color, new PaintPoint(run.X, run.Y), new Core.Size(60, 22));
                }

                _ = targetA.GetPixelBytes(0, 0, 1, 1);
                timesA.Add(Ms(started));
            }

            var pixelsA = targetA.GetPixelBytes();
            report.AppendLine($"A  readback path        : median {Median(timesA):F1} ms, p95 {P95(timesA):F1} ms, first {timesA[0]:F1} ms");

            // ---- path B: bitmaps of the stripes' alpha and of ink*alpha, summed into two layers, one shader pass over the backdrop
            var shader = await File.ReadAllBytesAsync(shaderPath, TestContext.Current.CancellationToken);
            using var back = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
            using var inkLayer = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
            using var alphaLayer = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
            using var result = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
            using var finalTarget = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);

            var coldStarted = Stopwatch.GetTimestamp();
            var alphaBitmaps = new List<CanvasBitmap>();
            var inkBitmaps = new List<CanvasBitmap>();
            for (var i = 0; i < runs.Count; i++)
            {
                var mask = masks[i];
                if (mask.Width == 0)
                {
                    alphaBitmaps.Add(null!);
                    inkBitmaps.Add(null!);
                    continue;
                }

                var alphas = Alphas(mask, runs[i].Color);
                alphaBitmaps.Add(CanvasBitmap.CreateFromBytes(device, alphas, mask.Width, mask.Height, DirectXPixelFormat.R8G8B8A8UIntNormalized, 96, CanvasAlphaMode.Ignore));
                inkBitmaps.Add(CanvasBitmap.CreateFromBytes(device, InkTimesAlpha(alphas, runs[i].Color), mask.Width, mask.Height, DirectXPixelFormat.R8G8B8A8UIntNormalized, 96, CanvasAlphaMode.Ignore));
            }

            var coldBitmaps = Ms(coldStarted);
            report.AppendLine($"B  one-time bitmap creation for {runs.Count} words: {coldBitmaps:F1} ms ({coldBitmaps * 1000 / runs.Count:F0} us/word)");

            using var effect = new PixelShaderEffect(shader);
            var timesB = new List<double>();
            var composeOnly = new List<double>();
            for (var frame = 0; frame < Frames; frame++)
            {
                var started = Stopwatch.GetTimestamp();
                using (var session = back.CreateDrawingSession())
                    DrawBackdrop(session);

                using (var session = inkLayer.CreateDrawingSession())
                {
                    session.Clear(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    session.Blend = CanvasBlend.Add;
                    for (var i = 0; i < runs.Count; i++)
                    {
                        if (inkBitmaps[i] is { } bitmap)
                            session.DrawImage(bitmap, masks[i].Left, masks[i].Top);
                    }
                }

                using (var session = alphaLayer.CreateDrawingSession())
                {
                    session.Clear(Windows.UI.Color.FromArgb(255, 0, 0, 0));
                    session.Blend = CanvasBlend.Add;
                    for (var i = 0; i < runs.Count; i++)
                    {
                        if (alphaBitmaps[i] is { } bitmap)
                            session.DrawImage(bitmap, masks[i].Left, masks[i].Top);
                    }
                }

                effect.Source1 = back;
                effect.Source2 = inkLayer;
                effect.Source3 = alphaLayer;
                using (var session = result.CreateDrawingSession())
                    session.DrawImage(effect);

                // What the view then does: put the finished region on its surface.
                using (var session = finalTarget.CreateDrawingSession())
                {
                    session.Blend = CanvasBlend.Copy;
                    session.DrawImage(result);
                }

                _ = finalTarget.GetPixelBytes(0, 0, 1, 1);
                timesB.Add(Ms(started));
            }

            report.AppendLine($"B  shader path (warm)   : median {Median(timesB):F1} ms, p95 {P95(timesB):F1} ms, first {timesB[0]:F1} ms");

            // ---- same, but a frame that scrolls: the words are drawn at a new offset, the bitmaps are the same
            var pixelsB = finalTarget.GetPixelBytes();
            long sum = 0, count = 0, big = 0;
            var max = 0;
            for (var i = 0; i < pixelsA.Length; i += 4)
            {
                for (var c = 0; c < 3; c++)
                {
                    var d = Math.Abs(pixelsA[i + c] - pixelsB[i + c]);
                    sum += d;
                    count++;
                    if (d > max) max = d;
                    if (d > 2) big++;
                }
            }

            report.AppendLine($"A vs B pixels: mean abs diff {(double)sum / count:F3}, max {max}, channels differing by more than 2: {big} of {count} ({100.0 * big / count:F3}%)");
            report.AppendLine($"speed-up (median): {Median(timesA) / Median(timesB):F1}x");

            if (Flat)
            {
                // ---- path C: what a backdrop tracker would allow over a flat background: the finished bitmap per word, one Copy draw each
                var flatBytes = new byte[Width * Height * 4];
                for (var i = 0; i < flatBytes.Length; i += 4)
                {
                    flatBytes[i] = FlatColor.B;
                    flatBytes[i + 1] = FlatColor.G;
                    flatBytes[i + 2] = FlatColor.R;
                    flatBytes[i + 3] = 255;
                }

                var cStarted = Stopwatch.GetTimestamp();
                var finished = new List<CanvasBitmap?>();
                for (var i = 0; i < runs.Count; i++)
                {
                    var mask = masks[i];
                    if (mask.Width == 0)
                    {
                        finished.Add(null);
                        continue;
                    }

                    var behind = new byte[mask.Width * mask.Height * 4];
                    for (var o = 0; o < behind.Length; o += 4)
                    {
                        behind[o] = FlatColor.B;
                        behind[o + 1] = FlatColor.G;
                        behind[o + 2] = FlatColor.R;
                        behind[o + 3] = 255;
                    }

                    finished.Add(CanvasBitmap.CreateFromBytes(device, Win2dTextBlend.Lcd(mask, runs[i].Color, behind), mask.Width, mask.Height,
                        DirectXPixelFormat.B8G8R8A8UIntNormalized, 96, CanvasAlphaMode.Ignore));
                }

                report.AppendLine($"C  one-time bitmap creation (CPU blend + upload): {Ms(cStarted):F1} ms ({Ms(cStarted) * 1000 / runs.Count:F0} us/word)");

                using var targetC = new CanvasRenderTarget(device, Width, Height, 96, DirectXPixelFormat.B8G8R8A8UIntNormalized, CanvasAlphaMode.Ignore);
                var timesC = new List<double>();
                for (var frame = 0; frame < Frames; frame++)
                {
                    var started = Stopwatch.GetTimestamp();
                    using (var session = targetC.CreateDrawingSession())
                    {
                        DrawBackdrop(session);
                        session.Blend = CanvasBlend.Copy;
                        for (var i = 0; i < runs.Count; i++)
                        {
                            if (finished[i] is { } bitmap)
                                session.DrawImage(bitmap, masks[i].Left, masks[i].Top);
                        }
                    }

                    _ = targetC.GetPixelBytes(0, 0, 1, 1);
                    timesC.Add(Ms(started));
                }

                report.AppendLine($"C  tracker path (warm)  : median {Median(timesC):F1} ms, p95 {P95(timesC):F1} ms, first {timesC[0]:F1} ms");

                var pixelsC = targetC.GetPixelBytes();
                long diffSum = 0, diffCount = 0;
                var diffMax = 0;
                for (var i = 0; i < pixelsA.Length; i++)
                {
                    var d = Math.Abs(pixelsA[i] - pixelsC[i]);
                    diffSum += d;
                    diffCount++;
                    if (d > diffMax) diffMax = d;
                }

                report.AppendLine($"A vs C pixels: mean abs diff {(double)diffSum / diffCount:F3}, max {diffMax}");
                foreach (var bitmap in finished)
                    bitmap?.Dispose();
            }

            foreach (var bitmap in alphaBitmaps.Concat(inkBitmaps))
                bitmap?.Dispose();
        }
        catch (Exception ex)
        {
            report.AppendLine("FAILED: " + ex);
        }

        await File.WriteAllTextAsync(outPath, report.ToString(), TestContext.Current.CancellationToken);
    }
}
