using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <c>TransparencyPolicy.Flatten</c>: documents targeting PDF/A-1 or PDF/X-1a/X-3 that use transparency are generated, with the
    /// transparent regions rendered as opaque bitmaps, instead of being rejected.
    /// </summary>
    public class TransparencyFlatteningTests
    {
        private static readonly PdfDocumentMetadata Metadata = new() { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) };

        private static PdfGenerateConfig PdfA1(TransparencyPolicy policy = TransparencyPolicy.Flatten) => new()
        {
            PageSize = PageSize.A4,
            PdfAConformance = PdfAConformance.PdfA1B,
            Metadata = Metadata,
            TransparencyPolicy = policy,
            CompressContentStreams = false,
        };

        private static async Task<string> Generate(string bodyHtml, PdfGenerateConfig config)
        {
            var doc = await new PdfGenerator().GeneratePdf($"<html><body>{bodyHtml}</body></html>", config);
            var stream = new MemoryStream();
            doc.Save(stream);
            return System.Text.Encoding.Latin1.GetString(stream.ToArray());
        }

        /// <summary>The markers of a transparency group anywhere in the file: soft masks, non-opaque alphas, blend modes, transparency groups.</summary>
        private static bool HasTransparency(string pdf) =>
            Regex.IsMatch(pdf, @"/SMask|/ca\s+0?\.\d|/CA\s+0?\.\d|/BM\s*/(?!Normal|Compatible)|/S\s*/Transparency");

        public static TheoryData<string, string> TransparentContent => new()
        {
            { "<div style=\"width: 50px; height: 50px; background: #ff0000; opacity: 0.5;\"></div>", "opacity" },
            { "<div style=\"width: 50px; height: 50px; background: rgba(255,0,0,.4);\"></div>", "rgba background" },
            { "<p style=\"color: rgba(0,0,0,.5)\">Half-transparent text</p>", "rgba text" },
            { """<svg viewBox="0 0 100 100" width="100" height="100"><rect width="50" height="50" fill="url(#g)"/><defs><linearGradient id="g"><stop offset="0" stop-color="red"/><stop offset="1" stop-color="blue" stop-opacity="0.5"/></linearGradient></defs></svg>""", "alpha gradient" },
            { """<svg viewBox="0 0 100 100" width="100" height="100"><rect width="50" height="50" fill="red" fill-opacity="0.5"/></svg>""", "fill-opacity" },
            { "<div style=\"width: 50px; height: 50px; background: #ff0000;\"></div><div style=\"width: 50px; height: 50px; margin-top: -25px; background: #00ff00; mix-blend-mode: multiply;\"></div>", "mix-blend-mode" },
            { "<div style=\"width: 50px; height: 50px; background: #ff0000; filter: brightness(1.5);\"></div>", "filter: brightness()" },
            { "<div style=\"width: 50px; height: 50px; background: #ff0000; filter: blur(2px);\"></div>", "filter: blur()" },
            { "<div style=\"width: 50px; height: 50px; background: #ff0000; filter: grayscale(50%);\"></div>", "filter: grayscale()" },
            { "<div style=\"width: 50px; height: 50px; background: #fff; box-shadow: 0 4px 10px #000\"></div>", "blurred box-shadow" },
            { "<p style=\"text-shadow: 2px 2px 4px #000\">Shadowed</p>", "blurred text-shadow" },
            { "<div style=\"width: 50px; height: 50px; background: #ff0000; backdrop-filter: blur(2px);\"></div>", "backdrop-filter" },
            { """<svg viewBox="0 0 100 100" width="100" height="100"><defs><filter id="f"><feColorMatrix type="matrix" values="1.5 0 0 0 0  0 1.5 0 0 0  0 0 1.5 0 0  0 0 0 1 0"/></filter></defs><rect width="50" height="50" fill="red" filter="url(#f)"/></svg>""", "svg filter" },
            { """<svg viewBox="0 0 100 100" width="100" height="100"><defs><filter id="f"><feGaussianBlur stdDeviation="3"/></filter></defs><rect width="50" height="50" fill="red" filter="url(#f)"/></svg>""", "svg blur" },
        };

        [Theory]
        [MemberData(nameof(TransparentContent))]
        public async Task PdfA1_WithFlatten_GeneratesADocumentWithNoTransparency(string body, string _)
        {
            var pdf = await Generate(body, PdfA1());

            Assert.False(HasTransparency(pdf), "the file must contain no transparency construct");
            Assert.Matches(@"/Subtype\s*/Image", pdf);
        }

        [Theory]
        [MemberData(nameof(TransparentContent))]
        public async Task PdfA1_WithReject_StillThrows(string body, string what)
        {
            // A backdrop-filter bitmap is opaque (it sits over white paper), so it needs no soft mask and is legal as it is.
            if (what == "backdrop-filter")
                return;

            await Assert.ThrowsAnyAsync<InvalidOperationException>(() => Generate(body, PdfA1(TransparencyPolicy.Reject)));
        }

        [Fact]
        public async Task ABackdropFilter_IsLegalUnderPdfA1WithoutFlattening_BecauseItsBitmapIsOpaque()
        {
            var pdf = await Generate("<div style=\"width: 50px; height: 50px; background: #ff0000; backdrop-filter: blur(2px);\"></div>", PdfA1(TransparencyPolicy.Reject));

            Assert.False(HasTransparency(pdf));
            Assert.Matches(@"/Subtype\s*/Image", pdf);
        }

        [Fact]
        public async Task ContentWithoutTransparency_StaysVector()
        {
            var pdf = await Generate("<div style=\"width: 50px; height: 50px; background: #ff0000;\"></div><p>Plain text</p>", PdfA1());

            Assert.DoesNotMatch(@"/Subtype\s*/Image", pdf);
        }

        [Fact]
        public async Task OnlyTheTransparentRegionIsABitmap_TheRestStaysVector()
        {
            const string body = """
                <div style="width:100px;height:40px;background:#123456"></div>
                <div style="width:100px;height:40px;background:rgba(255,0,0,.5)"></div>
                <div style="width:100px;height:40px;background:#654321"></div>
                """;
            var pdf = await Generate(body, PdfA1());

            // One flattened region: exactly one image, and its placement is about one box, not the whole page.
            var placements = Regex.Matches(pdf, @"q\s+([-\d.]+)\s+0\s+0\s+([-\d.]+)\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q");
            var placed = Assert.Single(placements);
            Assert.True(double.Parse(placed.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) < 120);
            Assert.False(HasTransparency(pdf));
        }

        [Fact]
        public async Task TextInAFlattenedRegion_StaysSelectable()
        {
            var pdf = await Generate("<div style=\"opacity:.5\"><p>Findable words</p></div>", PdfA1());

            Assert.Contains("3 Tr", pdf);
            Assert.False(HasTransparency(pdf));
        }

        [Fact]
        public async Task TheFlattenedBitmap_HasNoAlphaPlane()
        {
            var pdf = await Generate("<div style=\"width: 50px; height: 50px; background: rgba(255,0,0,.5);\"></div>", PdfA1());

            Assert.DoesNotContain("/SMask", pdf);
        }

        [Fact]
        public async Task ATransformedBox_CannotBeFlattened_AndIsStillRejected()
        {
            await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
                Generate("<div style=\"transform: rotate(3deg); width: 50px; height: 50px; opacity: .5; background: #f00\"></div>", PdfA1()));
        }

        [Fact]
        public async Task ResolutionFollowsRasterizationDpi_AndPlacementIsUnchanged()
        {
            const string body = "<div style=\"width: 100px; height: 100px; background: rgba(255,0,0,.5)\"></div>";
            var low = PdfA1();
            low.RasterizationDpi = 150;
            var high = PdfA1();
            high.RasterizationDpi = 300;

            var lowPdf = await Generate(body, low);
            var highPdf = await Generate(body, high);

            int Width(string pdf) => int.Parse(Regex.Match(pdf, @"/Width\s+(\d+)").Groups[1].Value);
            Assert.InRange((double)Width(highPdf) / Width(lowPdf), 1.9, 2.1);
        }

        [Fact]
        public async Task PdfX1a_FlattensToDeviceCmyk()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfXConformance = PdfXConformance.X1a,
                Metadata = Metadata,
                TransparencyPolicy = TransparencyPolicy.Flatten,
                CompressContentStreams = false,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfile(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var pdf = await Generate("<div style=\"width: 50px; height: 50px; background: #00aa00; opacity: .5\"></div>", config);

            Assert.False(HasTransparency(pdf));
            Assert.Matches(@"/ColorSpace\s*/DeviceCMYK", pdf);
            Assert.DoesNotMatch(@"/ColorSpace\s*/DeviceRGB", pdf);
        }

        [Fact]
        public async Task PdfX3_WithFlatten_GeneratesWithoutTransparency()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfXConformance = PdfXConformance.X3,
                Metadata = Metadata,
                TransparencyPolicy = TransparencyPolicy.Flatten,
                CompressContentStreams = false,
                ColorOptions = new ColorOptions
                {
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfile(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var pdf = await Generate("<div style=\"width: 50px; height: 50px; background: #ff0000; opacity: .5\"></div>", config);

            Assert.False(HasTransparency(pdf));
        }

        [Fact]
        public async Task ConformanceThatPermitsTransparency_IsUnaffectedByThePolicy()
        {
            var config = PdfA1();
            config.PdfAConformance = PdfAConformance.PdfA2B;

            var pdf = await Generate("<div style=\"width: 50px; height: 50px; background: #ff0000; opacity: 0.5;\"></div>", config);

            // PDF/A-2 permits transparency, so it is emitted as ordinary vector transparency rather than flattened.
            Assert.True(HasTransparency(pdf));
            Assert.DoesNotMatch(@"/Subtype\s*/Image", pdf);
        }
    }
}
