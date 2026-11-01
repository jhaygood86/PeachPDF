using PeachDrawing.Core;
using PeachDrawing.Win2D;
using PeachImage.Formats.Png;
using System.Collections.Concurrent;

namespace PeachDrawing.Win2D.Tests;

/// <summary>The two image decoders a context can use, and that they agree.</summary>
public class ImageCodecTests
{
    private static byte[] TwoColourPng()
    {
        var raster = new RasterRenderContext();
        using var canvas = raster.CreateCanvas(8, 4);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0)), 0, 0, 4, 4);
        canvas.DrawRectangle(raster.GetSolidBrush(PaintColor.FromArgb(255, 0, 0, 255)), 4, 0, 4, 4);
        using var stream = new MemoryStream();
        canvas.Save(stream, "png", new PngEncoderOptions());
        return stream.ToArray();
    }

    [Fact]
    public void PeachImage_is_the_default_codec_and_fallback_defaults_on()
    {
        using var context = Win2dRenderContext.CreateSoftware();

        Assert.Equal(Win2dImageCodec.PeachImage, context.ImageCodec);
        Assert.True(context.FallbackToPeachImage);
    }

    [Fact]
    public void The_native_codec_decodes_an_image_that_draws_at_its_destination()
    {
        using var context = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.Native);
        context.PixelsPerPoint = 1;
        using var scene = new Scene(context, 40, 40);
        using var image = context.ImageFromStream(new MemoryStream(TwoColourPng()));

        Assert.Equal(8, image.Width);
        Assert.Equal(4, image.Height);
        scene.Canvas.DrawImage(image, new Rect(0, 0, 40, 20), ImageSampling.Nearest);

        Assert.True(scene.IsRed(5, 10));
        Assert.True(scene.Px(35, 10) is { B: > 200, A: > 200 });
        Assert.True(scene.IsClear(20, 30));
    }

    [Fact]
    public void Both_codecs_decode_the_same_pixels()
    {
        using var peach = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.PeachImage);
        using var native = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.Native);
        var bytes = TwoColourPng();

        using var a = peach.ImageFromStream(new MemoryStream(bytes));
        using var b = native.ImageFromStream(new MemoryStream(bytes));

        var left = a.GetPixels()!.Value;
        var right = b.GetPixels()!.Value;
        Assert.Equal(left.Width, right.Width);
        Assert.Equal(left.Height, right.Height);
        Assert.True(left.PremultipliedRgba.Span.SequenceEqual(right.PremultipliedRgba.Span));
    }

    [Fact]
    public void The_native_codec_refuses_what_neither_decoder_knows()
    {
        using var context = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.Native);
        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([9, 9, 9, 9])));

        context.FallbackToPeachImage = false;
        Assert.Throws<InvalidDataException>(() => context.ImageFromStream(new MemoryStream([9, 9, 9, 9])));
    }

    [Fact]
    public async Task Native_decoding_does_not_deadlock_a_single_threaded_caller()
    {
        using var context = Win2dRenderContext.CreateSoftware(imageCodec: Win2dImageCodec.Native);
        var bytes = TwoColourPng();

        // A caller that blocks its own thread on the decode (as a UI thread would) still gets the image.
        var decode = Task.Factory.StartNew(() => context.ImageFromStream(new MemoryStream(bytes)),
            TestContext.Current.CancellationToken, TaskCreationOptions.None, new SingleThreadScheduler());

        using var image = await decode.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.Equal(8, image.Width);
    }

    private sealed class SingleThreadScheduler : TaskScheduler
    {
        private readonly BlockingCollection<Task> _queue = [];

        public SingleThreadScheduler() => new Thread(() =>
        {
            foreach (var task in _queue.GetConsumingEnumerable())
                TryExecuteTask(task);
        }) { IsBackground = true }.Start();

        protected override IEnumerable<Task> GetScheduledTasks() => _queue;

        protected override void QueueTask(Task task) => _queue.Add(task);

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) => false;
    }
}
