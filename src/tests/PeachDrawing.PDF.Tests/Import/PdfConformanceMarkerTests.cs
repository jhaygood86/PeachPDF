using PeachDrawing.PDF;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>The PDF/A and PDF/X claims, output intent and language of a file, read back from files the writer produced.</summary>
    public class PdfConformanceMarkerTests
    {
        private static byte[] Build(Action<PdfComposerOptions> configure)
        {
            var options = new PdfComposerOptions { CompressContentStreams = false, Title = "T", CreationDate = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero) };
            configure(options);
            using var composer = new PdfComposer(new PdfRenderContext(), options);
            composer.AddPage(100, 100);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        [Theory]
        [InlineData(PdfAConformance.PdfA1B)]
        [InlineData(PdfAConformance.PdfA2B)]
        [InlineData(PdfAConformance.PdfA2U)]
        [InlineData(PdfAConformance.PdfA3B)]
        public void PdfA_LevelIsReadFromTheXmp(PdfAConformance level)
        {
            var pdf = Build(o => (o.Conformance, o.Language) = (level, "en"));

            Assert.Equal(level, PdfPageSource.Open(pdf).GetConformance().PdfA);
        }

        [Fact]
        public void PdfX_LevelIsReadAlongWithItsOutputIntent()
        {
            var profile = IccProfileFixture.BuildRgbProfile();
            var pdf = Build(o =>
            {
                o.XConformance = PdfXConformance.X4;
                o.ColorOptions = new ColorOptions { OutputIntentProfile = profile, OutputIntentIdentifier = "Test condition" };
            });
            var source = PdfPageSource.Open(pdf);

            Assert.Equal(PdfXConformance.X4, source.GetConformance().PdfX);
            var intent = source.GetOutputIntent();
            Assert.NotNull(intent);
            Assert.Equal("Test condition", intent!.Identifier);
            Assert.Equal(profile, intent.Profile);
        }

        [Fact]
        public void OutputIntent_WithoutAConformanceLevel_IsWrittenWithoutAClaim()
        {
            var profile = IccProfileFixture.BuildRgbProfile();
            var pdf = Build(o => o.ColorOptions = new ColorOptions { OutputIntentProfile = profile, OutputIntentIdentifier = "Plain" });
            var source = PdfPageSource.Open(pdf);

            Assert.Equal("Plain", source.GetOutputIntent()!.Identifier);
            Assert.Equal(profile, source.GetOutputIntent()!.Profile);
            Assert.Equal(new PdfSourceConformance(PdfAConformance.None, PdfXConformance.None), source.GetConformance());
        }

        [Fact]
        public void OutputIntent_WithoutAnIdentifier_IsNamedCustom_AndAnUnreadableProfileIsRefused()
        {
            var pdf = Build(o => o.ColorOptions = new ColorOptions { OutputIntentProfile = IccProfileFixture.BuildRgbProfile() });
            Assert.Equal("Custom", PdfPageSource.Open(pdf).GetOutputIntent()!.Identifier);

            Assert.Throws<InvalidOperationException>(() => Build(o => o.ColorOptions = new ColorOptions { OutputIntentProfile = [1, 2, 3, 4] }));
        }

        [Fact]
        public void PlainFile_HasNoMarkers()
        {
            var source = PdfPageSource.Open(TestPdf.Build("0 g"));

            Assert.Equal(new PdfSourceConformance(PdfAConformance.None, PdfXConformance.None), source.GetConformance());
            Assert.Null(source.GetOutputIntent());
            Assert.Null(source.GetMetadata().Language);
        }

        [Fact]
        public void Language_IsRead()
        {
            var pdf = Build(o => o.Language = "fr-CA");

            Assert.Equal("fr-CA", PdfPageSource.Open(pdf).GetMetadata().Language);
        }

        [Fact]
        public void OutputIntent_WithAnUnreadableProfile_IsIgnored()
        {
            var pdf = System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(TestPdf.Build("0 g",
                extraObjects: [TestPdf.Stream("/N 3", "not an icc profile")]))
                .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /OutputIntents [<< /Type /OutputIntent /S /GTS_PDFA1 /OutputConditionIdentifier (x) /DestOutputProfile 5 0 R >>]"));

            Assert.Null(PdfPageSource.Open(pdf).GetOutputIntent());
        }
    }
}
