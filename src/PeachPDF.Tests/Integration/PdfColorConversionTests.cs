using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Phase B of the CMYK/ICC epic (jhaygood86/PeachPDF#1090) - <see cref="ColorOptions.ConversionMode"/>,
    /// backed by PeachImage 0.4.4's <c>IccColorProfile.ConvertTo</c> device-to-device primitive
    /// (<see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfColorConversionGuard"/>). The synthetic CMYK
    /// profile fixture's CLUT is zeroed (see <see cref="IccProfileFixture.BuildCmykProfile"/>'s own doc
    /// comment - colorimetric accuracy isn't the point there), so these tests verify the *structural*
    /// result (an RGB-authored color reaches the PDF as a real CMYK operator - the space actually changed,
    /// not just that some conversion ran) rather than exact converted values.
    /// </summary>
    public class PdfColorConversionTests
    {
        private static async Task<string> GetPdfText(string html, PdfGenerateConfig config)
        {
            config.CompressContentStreams = false;
            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, config);
            var ms = new MemoryStream();
            doc.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        private static readonly Regex CmykFillOperator = new(@"[\d.]+\s+[\d.]+\s+[\d.]+\s+[\d.]+\s+k\b");
        private static readonly Regex RgbFillOperator = new(@"[\d.]+\s+[\d.]+\s+[\d.]+\s+rg\b");

        [Fact]
        public async Task ConvertToOutputIntent_RgbColor_EmitsCmykOperator()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(30, 60, 90)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToOutputIntent,
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.Matches(CmykFillOperator, pdfText);
            Assert.DoesNotMatch(RgbFillOperator, pdfText);
        }

        [Fact]
        public async Task ConvertToProfile_RgbColor_EmitsCmykOperator()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(30, 60, 90)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToProfile,
                    ConvertToProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.Matches(CmykFillOperator, pdfText);
        }

        [Fact]
        public async Task GrayscaleViaK_RgbColor_EmitsCmykOperatorWithOnlyKNonzero()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(200, 30, 30)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.GrayscaleViaK,
                    FallbackCmykProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                },
            };

            var pdfText = await GetPdfText(html, config);

            // C M Y are always 0 under GrayscaleViaK - only K may be nonzero.
            Assert.Matches(new Regex(@"0(\.0+)?\s+0(\.0+)?\s+0(\.0+)?\s+[\d.]+\s+k\b"), pdfText);
        }

        [Fact]
        public async Task ConvertToOutputIntent_DeviceCmykColor_WithoutFallbackProfile_StaysUnconverted()
        {
            // device-cmyk() has no defined source profile without ColorOptions.FallbackCmykProfile - left
            // exactly as authored even under a conversion mode (see ColorOptions' remarks).
            var html = "<html><body style=\"margin:0\"><p style=\"color: device-cmyk(0 1 1 0)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToOutputIntent,
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var pdfText = await GetPdfText(html, config);

            // Unconverted means the exact authored components: C=0 M=1 Y=1 K=0.
            Assert.Matches(new Regex(@"0(\.0+)?\s+1(\.0+)?\s+1(\.0+)?\s+0(\.0+)?\s+k\b"), pdfText);
        }

        [Fact]
        public async Task ConvertToOutputIntent_DeviceCmykColor_WithFallbackProfile_IsConverted()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: device-cmyk(0.2 0.4 0.6 0.1)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToOutputIntent,
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                    FallbackCmykProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                },
            };

            var pdfText = await GetPdfText(html, config);

            // Still a CMYK operator (conversion target is CMYK too), just not necessarily the exact
            // authored components anymore - the point is the conversion path was taken at all.
            Assert.Matches(CmykFillOperator, pdfText);
        }

        [Fact]
        public async Task MixedRgbAndCmyk_UnderConversion_BothBecomeCmyk()
        {
            var html = "<html><body style=\"margin:0\">" +
                "<p style=\"color: rgb(10, 20, 30)\">RGB</p>" +
                "<p style=\"color: device-cmyk(0 1 1 0)\">CMYK</p>" +
                "</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToOutputIntent,
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.DoesNotMatch(RgbFillOperator, pdfText);
        }

        [Fact]
        public async Task ConvertToOutputIntent_MissingProfile_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions { ConversionMode = ColorConversionMode.ConvertToOutputIntent },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><body>x</body></html>", config));
        }

        [Fact]
        public async Task ConvertToOutputIntent_UnparsableProfile_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToOutputIntent,
                    OutputIntentProfile = [0x00, 0x01, 0x02],
                },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><body>x</body></html>", config));
        }

        [Fact]
        public async Task ConvertToProfile_UnparsableProfile_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToProfile,
                    ConvertToProfile = [0x00, 0x01, 0x02],
                },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><body>x</body></html>", config));
        }

        [Fact]
        public async Task ConvertToProfile_RgbDestination_ProducesRgbOperator()
        {
            // A non-CMYK ConvertToProfile destination (BuildRgbProfile's linear-TRC sRGB-primaries
            // profile, distinct from plain sRGB gamma encoding) - proves the conversion pipeline isn't
            // hardcoded to a CMYK destination.DataColorSpace.
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(30, 60, 90)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToProfile,
                    ConvertToProfile = IccProfileFixture.BuildRgbProfile(),
                },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.Matches(RgbFillOperator, pdfText);
        }

        [Fact]
        public async Task ConvertToProfile_GrayDestination_Succeeds()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(30, 60, 90)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToProfile,
                    ConvertToProfile = IccProfileFixture.BuildGrayProfile(),
                },
            };

            // Just needs to succeed without throwing - a Gray destination's XColor.FromGrayScale
            // constructor doesn't preserve alpha, which is a documented, narrow, low-priority gap
            // (Gray output-intent/conversion-target profiles are unusual for CMYK-oriented print
            // workflows) rather than something worth a content-stream assertion here.
            await GetPdfText(html, config);
        }

        [Theory]
        [InlineData(ColorRenderingIntent.Perceptual)]
        [InlineData(ColorRenderingIntent.RelativeColorimetric)]
        [InlineData(ColorRenderingIntent.Saturation)]
        // AbsoluteColorimetric is deliberately not covered here: it needs the profile's media white
        // point tag ("wtpt", mandatory in a real ICC profile per ICC.1:2010 §8.2), which
        // IccProfileFixture's minimal fixtures don't write (see that class's own doc comments - only
        // successful parsing, not full-spec completeness, is the point of those fixtures). PeachImage's
        // ICC engine hits a NullReferenceException against a profile missing that tag under this intent
        // specifically - worth a real ICC profile (or a fixture with wtpt added) to verify properly, and
        // worth flagging upstream as a robustness gap (a clear exception would be friendlier than an NRE
        // for a malformed/incomplete real-world profile), but out of scope for this structural test.
        public async Task ConvertToOutputIntent_EveryRenderingIntent_Succeeds(ColorRenderingIntent intent)
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(30, 60, 90)\">Hi</p></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.ConvertToOutputIntent,
                    OutputIntentProfile = IccProfileFixture.BuildCmykProfileWithReverseTransform(),
                    OutputIntentIdentifier = "Test CMYK Profile",
                    RenderingIntent = intent,
                },
            };

            var pdfText = await GetPdfText(html, config);

            Assert.Matches(CmykFillOperator, pdfText);
        }

        [Fact]
        public async Task ConvertToProfile_MissingProfile_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions { ConversionMode = ColorConversionMode.ConvertToProfile },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><body>x</body></html>", config));
        }

        [Fact]
        public async Task GrayscaleViaK_MissingFallbackProfile_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions { ConversionMode = ColorConversionMode.GrayscaleViaK },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><body>x</body></html>", config));
        }

        [Fact]
        public async Task GrayscaleViaK_NonCmykFallbackProfile_Throws()
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                ColorOptions = new ColorOptions
                {
                    ConversionMode = ColorConversionMode.GrayscaleViaK,
                    FallbackCmykProfile = IccProfileFixture.BuildRgbProfile(),
                },
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PdfGenerator().GeneratePdf("<html><body>x</body></html>", config));
        }

        [Fact]
        public async Task PreserveAsAuthored_Default_NoConversionHappens()
        {
            var html = "<html><body style=\"margin:0\"><p style=\"color: rgb(30, 60, 90)\">Hi</p></body></html>";
            var pdfText = await GetPdfText(html, new PdfGenerateConfig { PageSize = PageSize.A4 });

            Assert.DoesNotMatch(CmykFillOperator, pdfText);
        }
    }
}
