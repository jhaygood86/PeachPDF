using System.Runtime.InteropServices;
using Android.Graphics;
using Android.Media;
using PeachBrackets.Media.Android.Internal;
using PeachBrackets.Media;
using PeachDrawing.Core;
using PeachDrawing.Skia;
using SkiaSharp;

namespace PeachBrackets.Media.Android.Example;

/// <summary>
/// The zero-copy path on a real GL stack: an offscreen EGL context with a Skia <see cref="GRContext"/>, a player rendering into a PRIVATE-format reader
/// the GPU can sample, the reader's hardware buffer imported as a texture, and that texture drawn onto a GPU surface and read back.
/// </summary>
internal static partial class GpuCheck
{
    [LibraryImport("libEGL.so")]
    private static partial nint eglGetDisplay(nint native);

    [LibraryImport("libEGL.so")]
    private static unsafe partial int eglInitialize(nint display, int* major, int* minor);

    [LibraryImport("libEGL.so")]
    private static unsafe partial int eglChooseConfig(nint display, int* attributes, nint* configs, int size, int* count);

    [LibraryImport("libEGL.so")]
    private static unsafe partial nint eglCreateContext(nint display, nint config, nint share, int* attributes);

    [LibraryImport("libEGL.so")]
    private static unsafe partial nint eglCreatePbufferSurface(nint display, nint config, int* attributes);

    [LibraryImport("libEGL.so")]
    private static partial int eglMakeCurrent(nint display, nint draw, nint read, nint context);

    public static Task<string?> RunAsync(Func<string, System.IO.Stream> open)
    {
        // A GL context belongs to one thread, so everything here, including the waiting, happens on one that nothing else uses.
        var done = new TaskCompletionSource<string?>();
        new Thread(() =>
        {
            try
            {
                done.SetResult(Run(open));
            }
            catch (Exception ex)
            {
    done.SetResult(ex.ToString());
            }
        }) { IsBackground = true }.Start();
        return done.Task;
    }

    private static string? Run(Func<string, System.IO.Stream> open)
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(29) || !HardwareBufferImporter.IsSupported())
            return "the device has no hardware buffer import";

        var problem = MakeCurrent(out var display);
        if (problem is not null)
            return problem;

        try
        {
            using var gr = GRContext.CreateGl();
            if (gr is null)
                return "no Skia GL context";

            using var context = new SkiaRenderContext { GpuContext = gr };
            return KnownBuffer(gr, context) ?? PlayIntoTexture(gr, context, open);
        }
        finally
        {
            eglMakeCurrent(display, 0, 0, 0);
        }
    }

    private static unsafe string? MakeCurrent(out nint display)
    {
        display = eglGetDisplay(0);
        int major, minor;
        if (display == 0 || eglInitialize(display, &major, &minor) == 0)
            return "no EGL display";

        var config = stackalloc nint[1];
        int count;
        var wanted = stackalloc int[] { 0x3033, 1, 0x3040, 4, 0x3038 }; // EGL_SURFACE_TYPE pbuffer, EGL_RENDERABLE_TYPE ES2, EGL_NONE
        if (eglChooseConfig(display, wanted, config, 1, &count) == 0 || count == 0)
            return "no EGL config";

        var contextAttributes = stackalloc int[] { 0x3098, 2, 0x3038 }; // EGL_CONTEXT_CLIENT_VERSION 2
        var context = eglCreateContext(display, config[0], 0, contextAttributes);
        var surfaceAttributes = stackalloc int[] { 0x3057, 16, 0x3056, 16, 0x3038 }; // 16 x 16 pbuffer
        var surface = eglCreatePbufferSurface(display, config[0], surfaceAttributes);
        return context == 0 || surface == 0 || eglMakeCurrent(display, surface, surface, context) == 0 ? "no EGL context" : null;
    }

    [LibraryImport("libandroid.so")]
    private static unsafe partial int AHardwareBuffer_lock(nint buffer, ulong usage, int fence, nint rect, byte** address);

    [LibraryImport("libandroid.so")]
    private static unsafe partial int AHardwareBuffer_unlock(nint buffer, nint fence);

    [LibraryImport("libandroid.so")]
    private static partial nint AHardwareBuffer_fromHardwareBuffer(nint env, nint hardwareBuffer);

    // A hardware buffer the CPU fills with a known gradient, imported and drawn: the importer alone, with no decoder in it.
    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static unsafe string? KnownBuffer(GRContext gr, SkiaRenderContext context)
    {
        using var buffer = global::Android.Hardware.HardwareBuffer.Create(64, 48, global::Android.Hardware.HardwareBufferFormat.Rgba8888, 1, (global::Android.Hardware.HardwareBufferUsage)(0x30 | 0x100));
        if (buffer is null)
            return "no buffer";

        var native = AHardwareBuffer_fromHardwareBuffer(global::Android.Runtime.JNIEnv.Handle, buffer.Handle);
        byte* address;
        if (AHardwareBuffer_lock(native, 0x30, -1, 0, &address) != 0)
            return "no lock";

        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var o = ((y * 64) + x) * 4;
                address[o] = (byte)(x * 4);
                address[o + 1] = (byte)(y * 5);
                address[o + 2] = 128;
                address[o + 3] = 255;
            }
        }

        AHardwareBuffer_unlock(native, 0);
        using var image = HardwareBufferImporter.Import(gr, buffer, 64, 48);
        if (image is null)
            return "no import";

        var info = new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(gr, true, info);
        surface!.Canvas.DrawImage(image, 0, 0);
        surface.Canvas.Flush();
        using var bitmap = new SKBitmap(info);
        surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
        var worst = 0;
        for (var y = 0; y < 48; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                var p = bitmap.GetPixel(x, y);
                worst = Math.Max(worst, Math.Max(Math.Abs(p.Red - (x * 4)), Math.Max(Math.Abs(p.Green - (y * 5)), Math.Abs(p.Blue - 128))));
            }
        }

        global::Android.Util.Log.Info("PEACH_MEDIA", "known buffer: worst difference " + worst + ", pixel (10,10) " + bitmap.GetPixel(10, 10));
        return worst > 2 ? "the imported texture differs by " + worst : null;
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android29.0")]
    private static string? PlayIntoTexture(GRContext gr, SkiaRenderContext context, Func<string, System.IO.Stream> open)
    {
        var request = new MediaRequest(new Uri("https://media.test/moving.mp4"), MediaKind.Video, context,
            _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(open("moving.mp4"), null))) { Muted = true };
        using var session = new AndroidMediaProvider().OpenAsync(request).AsTask().GetAwaiter().GetResult();
        if (session is null)
            return "did not open";

        session.Play();
        MediaFrame? frame = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            Thread.Sleep(100);
            frame = session.AcquireFrame();
            if (frame is not null && frame.Time > TimeSpan.FromMilliseconds(300))
                break;

            frame?.Dispose();
            frame = null;
        }

        if (frame is null)
            return "no picture arrived";

        using (frame)
        {
            if (frame.Image.GetType().Name != "SkiaImage")
                return "the picture is a " + frame.Image.GetType().Name;

            var info = new SKImageInfo(64, 48, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(gr, true, info);
            if (surface is null)
                return "no GPU surface";

            using (var canvas = SkiaCanvas.FromCanvas(context, surface.Canvas, new PeachDrawing.Core.Rect(0, 0, 64, 48)))
                canvas.DrawImage(frame.Image, new PeachDrawing.Core.Rect(0, 0, 64, 48));

            surface.Canvas.Flush();
            using var drawn = new SKBitmap(info);
            if (!surface.ReadPixels(info, drawn.GetPixels(), info.RowBytes, 0, 0))
                return "no readback";

            // What the platform itself decodes at that time, with no GPU in it.
            var truth = new AndroidMediaProvider(new MediaProviderOptions { DeterministicClock = true }).OpenAsync(new MediaRequest(new Uri("https://media.test/moving.mp4"), MediaKind.Video, context,
                _ => new ValueTask<RNetworkResponse?>(new RNetworkResponse(open("moving.mp4"), null))) { Muted = true }).AsTask().GetAwaiter().GetResult()!;
            truth.Seek(frame.Time);
            using var truthFrame = truth.AcquireFrame()!;
            var reference = truthFrame.Image.GetPixels()!.Value.PremultipliedRgba.Span;

            var distinct = new HashSet<uint>();
            long[] sums = new long[4];
            for (var y = 0; y < 48; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var p = drawn.GetPixel(x, y);
                    distinct.Add((uint)p);
                    for (var variant = 0; variant < 4; variant++)
                    {
                        var rx = (variant & 1) != 0 ? 63 - x : x;
                        var ry = (variant & 2) != 0 ? 47 - y : y;
                        var i = ((ry * 64) + rx) * 4;
                        sums[variant] += Math.Abs(p.Red - reference[i]) + Math.Abs(p.Green - reference[i + 1]) + Math.Abs(p.Blue - reference[i + 2]);
                    }
                }
            }

            // The right way up: any other orientation is far off.
            var worst = (int)(sums[0] / (64 * 48 * 3));
            // A picture that is nearly the same turned over (a bar in the middle) cannot say which way up it is; the known buffer above does that. Here a turned copy has to fit better than the upright one.
            if (sums[1] < sums[0] || sums[2] < sums[0])
                return "the texture is flipped";

            // The same picture read on the CPU (what a raster canvas or a readback gets) agrees too.
            var copy = frame.Image.GetPixels();
            if (copy is null)
                return "the buffer gave no pixels";

            var cpu = copy.Value.PremultipliedRgba.Span;
            long cpuError = 0;
            for (var i = 0; i < 64 * 48 * 4; i += 4)
                cpuError += Math.Abs(cpu[i] - reference[i]) + Math.Abs(cpu[i + 1] - reference[i + 1]) + Math.Abs(cpu[i + 2] - reference[i + 2]);

            if (cpuError / (64 * 48 * 3) > 6)
                return "the CPU copy differs by " + (cpuError / (64 * 48 * 3));

            // A canvas with no GPU context draws the same picture from those pixels.
            using var cpuContext = new SkiaRenderContext();
            using var cpuSurface = SKSurface.Create(info);
            using (var canvas = SkiaCanvas.FromCanvas(cpuContext, cpuSurface.Canvas, new PeachDrawing.Core.Rect(0, 0, 64, 48)))
                canvas.DrawImage(frame.Image, new PeachDrawing.Core.Rect(0, 0, 64, 48));

            using var cpuDrawn = new SKBitmap(info);
            cpuSurface.ReadPixels(info, cpuDrawn.GetPixels(), info.RowBytes, 0, 0);
            long drawnError = 0;
            for (var y = 0; y < 48; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var p = cpuDrawn.GetPixel(x, y);
                    var i = ((y * 64) + x) * 4;
                    drawnError += Math.Abs(p.Red - reference[i]) + Math.Abs(p.Green - reference[i + 1]) + Math.Abs(p.Blue - reference[i + 2]);
                }
            }

            if (drawnError / (64 * 48 * 3) > 6)
                return "a canvas with no GPU drew it off by " + (drawnError / (64 * 48 * 3));

            global::Android.Util.Log.Info("PEACH_MEDIA", $"gpu frame at {frame.Time.TotalMilliseconds} ms: {distinct.Count} colors, texture differs from the platform decode by {worst} on average");
            // Playing on to the end while drawing every picture that arrives: the buffers are handed back, nothing throws.
            var drawnFrames = 0;
            var end = DateTime.UtcNow.AddSeconds(20);
            while (!session.Ended && DateTime.UtcNow < end)
            {
                Thread.Sleep(60);
                using var next = session.AcquireFrame();
                if (next is null)
                    continue;

                using var again = SkiaCanvas.FromCanvas(context, surface.Canvas, new PeachDrawing.Core.Rect(0, 0, 64, 48));
                again.DrawImage(next.Image, new PeachDrawing.Core.Rect(0, 0, 64, 48));
                surface.Canvas.Flush();
                drawnFrames++;
            }

            if (!session.Ended || drawnFrames < 5)
                return $"played {drawnFrames} pictures and {(session.Ended ? "ended" : "did not end")}";

            return distinct.Count < 2 ? "the texture is one color" : worst > 6 ? "the texture differs from the platform decode by " + worst : null;
        }
    }
}
