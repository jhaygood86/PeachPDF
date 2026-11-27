using PeachBrackets.Media;
using PeachDrawing.Core;
using System.Text;

namespace PeachPDF.Tests.Integration
{
    /// <summary>A <c>video</c> in a PDF with a host's <see cref="IMediaProvider"/>: the picture of its media is written as an image.</summary>
    public class VideoPdfTests
    {
        private sealed class StillSession(Image frame, int width, int height, List<TimeSpan> requested) : IMediaSession
        {
            public MediaKind Kind => MediaKind.Video;

            public MediaReadyState ReadyState => MediaReadyState.EnoughData;

            public TimeSpan? Duration => TimeSpan.FromSeconds(5);

            public bool HasVideo => true;

            public int VideoWidth => width;

            public int VideoHeight => height;

            public TimeSpan Position => TimeSpan.Zero;

            public bool IsPlaying => false;

            public bool Ended => false;

            public MediaErrorCode Error => MediaErrorCode.None;

            public double PlaybackRate { get; set; } = 1;

            public double Volume { get; set; } = 1;

            public bool Muted { get; set; }

            public bool Loop { get; set; }

            public event EventHandler<MediaChangedEventArgs>? Changed
            {
                add { }
                remove { }
            }

            public void Play()
            {
            }

            public void Pause()
            {
            }

            public void Seek(TimeSpan position)
            {
            }

            public void Update(TimeSpan documentTime)
            {
            }

            public MediaFrame? AcquireFrame() => new(frame, TimeSpan.Zero);

            public ValueTask<MediaFrame?> AcquireFrameAtAsync(TimeSpan position, CancellationToken cancellationToken = default)
            {
                requested.Add(position);
                return new ValueTask<MediaFrame?>(AcquireFrame());
            }

            public void Dispose()
            {
            }
        }

        private sealed class StillProvider(List<TimeSpan> requested) : IMediaProvider
        {
            public string CanPlayType(string mimeType) => "probably";

            public ValueTask<IMediaSession?> OpenAsync(MediaRequest request, CancellationToken cancellationToken = default)
            {
                // The picture is made by the document's own render context, as a provider on a graphics device makes one on that device.
                var png = RasterPngFixture.MakeInterlacedPngBytes(32, 18, 200, 30, 30, interlace: false);
                using var stream = new MemoryStream(png);
                return new ValueTask<IMediaSession?>(new StillSession(request.RenderContext.ImageFromStream(stream), 32, 18, requested));
            }
        }

        private static async Task<string> PdfTextAsync(string html, PdfGenerateConfig config)
        {
            var doc = await new PdfGenerator().GeneratePdf(html, config);
            using var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public async Task AVideo_WithAMediaProvider_IsWrittenAsTheImageOfItsFirstPicture()
        {
            var requested = new List<TimeSpan>();
            var html = "<!DOCTYPE html><html><body><video src='https://media.test/clip.mp4#t=2'></video></body></html>";

            var text = await PdfTextAsync(html, new PdfGenerateConfig { PageSize = PageSize.A4, MediaProvider = new StillProvider(requested) });

            Assert.Contains("/Subtype /Image", text);
            Assert.Equal([TimeSpan.FromSeconds(2)], requested);
        }

        [Fact]
        public async Task AVideo_WithNoMediaProviderAndNoPoster_WritesNoImage()
        {
            var html = "<!DOCTYPE html><html><body><video src='https://media.test/clip.mp4'></video></body></html>";

            var text = await PdfTextAsync(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotContain("/Subtype /Image", text);
        }
    }
}
