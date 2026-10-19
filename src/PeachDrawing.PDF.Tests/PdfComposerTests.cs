using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>
    /// <see cref="PdfComposer"/> used the way a consumer of the package uses it. The files are written uncompressed so the drawing
    /// operators can be read back; each test would fail if the feature it names were a no-op.
    /// </summary>
    public class PdfComposerTests
    {
        private static readonly PaintColor Black = PaintColor.FromArgb(255, 0, 0, 0);
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 255, 0, 0);

        private static async Task<(PdfRenderContext Context, Font Font)> NewContextAsync(double size = 24)
        {
            var context = new PdfRenderContext();
            var name = "Composer-" + Guid.NewGuid().ToString("N");
            await using (var stream = File.OpenRead(BundledFonts.Ttf))
            {
                await context.AddFont(stream, name);
            }

            return (context, context.GetFont(name, size, PaintFontStyle.Regular)!);
        }

        private static string Utf16Hex(string text) => "FEFF" + Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(text));

        private static string Latin1(byte[] bytes) => Encoding.Latin1.GetString(bytes);

        private static byte[] Save(PdfComposer composer)
        {
            using var stream = new MemoryStream();
            composer.Save(stream);
            return stream.ToArray();
        }

        private static PdfComposerOptions Plain(Action<PdfComposerOptions>? configure = null)
        {
            var options = new PdfComposerOptions { CompressContentStreams = false };
            configure?.Invoke(options);
            return options;
        }

        [Fact]
        public async Task OnePage_WithAFilledRectangleAndText_IsAWellFormedPdf()
        {
            var (context, font) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain());

            var canvas = composer.AddPage(595, 842).Canvas;
            canvas.DrawRectangle(canvas.GetSolidBrush(Red), 50, 60, 200, 100);
            canvas.DrawString("Hello PDF", font, Black, new PaintPoint(50, 200), new Size(300, 30));

            var pdf = Latin1(Save(composer));

            Assert.StartsWith("%PDF-", pdf);
            Assert.Contains("%%EOF", pdf);
            Assert.Equal(1, composer.PageCount);
            Assert.Contains("/MediaBox", pdf);
            Assert.Contains("595", pdf);
            // The red fill: DeviceRGB colour 1 0 0 and a rectangle path, and the text as a shown glyph run in an embedded font.
            Assert.Contains("1 0 0 rg", pdf);
            Assert.Contains(" re", pdf);
            Assert.Contains(" Tj", pdf);
            Assert.Contains("/FontFile2", pdf);
        }

        [Fact]
        public async Task Pages_AreAddedInOrderWithTheirOwnSizes()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain());

            composer.AddPage(200, 300);
            composer.AddPage(400, 100);

            var pdf = Latin1(Save(composer));

            Assert.Equal(2, composer.PageCount);
            Assert.Contains("/MediaBox [0 0 200 300]", pdf);
            Assert.Contains("/MediaBox [0 0 400 100]", pdf);
            Assert.True(pdf.IndexOf("[0 0 200 300]", StringComparison.Ordinal) < pdf.IndexOf("[0 0 400 100]", StringComparison.Ordinal));
        }

        [Fact]
        public async Task TheCanvasDrawsInLayoutUnits_SoAContextWithTwoUnitsPerPointHalvesEveryCoordinate()
        {
            var (context, _) = await NewContextAsync();
            context.PixelsPerPoint = 2;
            using var composer = new PdfComposer(context, Plain());

            var canvas = composer.AddPage(100, 100).Canvas;
            // 200 x 200 layout units over a 100 x 100 point page: the whole page.
            canvas.DrawRectangle(canvas.GetSolidBrush(Red), 0, 0, 200, 200);

            var pdf = Latin1(Save(composer));

            Assert.Contains("0 0 100 100 re", pdf.Replace("0.0000", "0"));
        }

        [Fact]
        public async Task Options_AreWrittenToTheDocumentInformation()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain(o =>
            {
                o.Title = "A Plain Title";
                o.Author = "Some Author";
                o.Subject = "A Subject";
                o.Keywords = "alpha beta";
                o.Creator = "A Creator";
                o.CreationDate = new DateTimeOffset(2024, 3, 15, 10, 30, 0, TimeSpan.Zero);
            }));
            composer.AddPage(100, 100);

            var pdf = Latin1(Save(composer));

            // Text strings are written as UTF-16BE hex, which is how a reader finds them regardless of the characters in them.
            Assert.Contains("/Title <" + Utf16Hex("A Plain Title") + ">", pdf);
            Assert.Contains("/Author <" + Utf16Hex("Some Author") + ">", pdf);
            Assert.Contains("/Subject <" + Utf16Hex("A Subject") + ">", pdf);
            Assert.Contains("/Keywords <" + Utf16Hex("alpha beta") + ">", pdf);
            Assert.Contains("/Creator <" + Utf16Hex("A Creator") + ">", pdf);
            Assert.Contains("/CreationDate (D:2024", pdf);
            Assert.Contains("/Producer (PeachDrawing.PDF ", pdf);
        }

        [Theory]
        [InlineData(PdfVersion.Pdf20, "%PDF-2.0")]
        [InlineData(PdfVersion.Pdf17, "%PDF-1.4")]
        public async Task Version_SetsTheFileHeader(PdfVersion version, string header)
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain(o => o.Version = version));
            composer.AddPage(100, 100);

            Assert.StartsWith(header, Latin1(Save(composer)));
        }

        [Fact]
        public async Task CompressContentStreams_ShrinksTheFile()
        {
            var (context, _) = await NewContextAsync();
            byte[] Compose(bool compress)
            {
                using var composer = new PdfComposer(context, new PdfComposerOptions { CompressContentStreams = compress });
                var canvas = composer.AddPage(500, 500).Canvas;
                for (var i = 0; i < 200; i++)
                    canvas.DrawRectangle(canvas.GetSolidBrush(Red), i, i, 10, 10);
                return Save(composer);
            }

            var plain = Compose(compress: false);
            var packed = Compose(compress: true);

            Assert.True(packed.Length < plain.Length, $"compressed {packed.Length} should be smaller than plain {plain.Length}");
            Assert.Contains("/FlateDecode", Latin1(packed));
        }

        [Fact]
        public async Task Encryption_WritesAnEncryptDictionary_AndHidesTheDocumentInformation()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain(o =>
            {
                o.Title = "Secret Title";
                o.Version = PdfVersion.Pdf17;
                o.Encryption = new PdfEncryptionOptions { UserPassword = "user", OwnerPassword = "owner", Permissions = PdfPermissions.ReadOnly };
            }));
            composer.AddPage(100, 100);

            var pdf = Latin1(Save(composer));

            Assert.Contains("/Encrypt", pdf);
            Assert.DoesNotContain("Secret Title", pdf);
            // AES-256 needs at least a 1.7 header; the unencrypted 1.7 request would have written 1.4.
            Assert.StartsWith("%PDF-1.7", pdf);
        }

        [Fact]
        public async Task EncryptionOptions_AreSnapshotAtCreation()
        {
            var (context, _) = await NewContextAsync();
            var encryption = new PdfEncryptionOptions { UserPassword = "user" };
            using var composer = new PdfComposer(context, Plain(o => o.Encryption = encryption));
            composer.AddPage(100, 100);
            encryption.UserPassword = "changed";

            var first = Latin1(Save(composer));

            Assert.Contains("/Encrypt", first);
        }

        [Fact]
        public async Task SaveWithoutPages_Throws()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context);

            Assert.Throws<InvalidOperationException>(() => composer.Save(new MemoryStream()));
        }

        [Fact]
        public async Task AfterSave_PagesCanBeAdded_AndTheDocumentSavedAgain()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain());
            composer.AddPage(100, 100);
            var first = Latin1(Save(composer));

            composer.AddPage(200, 50);
            var second = Latin1(Save(composer));

            Assert.Contains("/Count 1", first);
            Assert.Contains("/Count 2", second);
            Assert.Contains("/MediaBox [0 0 200 50]", second);
        }

        [Theory]
        [InlineData(-5, 100)]
        [InlineData(100, -1)]
        [InlineData(double.NaN, 100)]
        [InlineData(100, double.PositiveInfinity)]
        public async Task AddPage_RejectsAnUnusableSize(double width, double height)
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context);

            Assert.Throws<ArgumentOutOfRangeException>(() => composer.AddPage(width, height));
        }

        [Fact]
        public async Task AZeroSizedPage_IsAnEmptyPage()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain());
            composer.AddPage(0, 0);

            Assert.Contains("/MediaBox [0 0 0 0]", Latin1(Save(composer)));
        }

        [Fact]
        public void NullArguments_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => new PdfComposer(null!));
            using var composer = new PdfComposer(new PdfRenderContext());
            Assert.Throws<ArgumentNullException>(() => composer.Save(null!));
        }

        [Fact]
        public async Task TextWidthMeasured_OnTheCanvasMatchesWhatTheContextsFontSays()
        {
            var (context, font) = await NewContextAsync(20);
            using var composer = new PdfComposer(context);
            var canvas = composer.AddPage(100, 100).Canvas;

            var narrow = canvas.MeasureString("i", font).Width;
            var wide = canvas.MeasureString("WWWW", font).Width;

            Assert.True(narrow > 0);
            Assert.True(wide > narrow * 4);
        }

        [Fact]
        public async Task ClippingAndTransformsNest_AndTheCanvasStaysUsable()
        {
            var (context, _) = await NewContextAsync();
            using var composer = new PdfComposer(context, Plain());
            var canvas = composer.AddPage(200, 200).Canvas;

            canvas.PushClip(new Rect(10, 10, 100, 100));
            canvas.PushTransform(System.Numerics.Matrix3x2.CreateRotation(0.5f));
            canvas.DrawRectangle(canvas.GetSolidBrush(Red), 0, 0, 50, 50);
            canvas.PopTransform();
            canvas.PopClip();

            var pdf = Latin1(Save(composer));

            Assert.Contains("W* n", pdf);
            Assert.Contains(" cm", pdf);
        }
    }
}
