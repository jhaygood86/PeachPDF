using PeachPDF.PdfSharpCore;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    public class SvgFormReuseTests
    {
        private static async Task<string> Render(string body)
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false };
            config.SetMargins(20);
            var doc = await new PdfGenerator().GeneratePdf(
                "<!DOCTYPE html><html><body style='margin:0'>" + body + "</body></html>", config);
            var stream = new MemoryStream();
            doc.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        private static string SvgDataUri(string fill = "#c0392b") =>
            "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                $"<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 20 20' width='20' height='20'>" +
                $"<rect width='20' height='20' fill='{fill}'/></svg>"));

        private static void AssertOneFormOnTwoPages(string pdf, bool tiled = false)
        {
            Assert.Equal(2, Regex.Matches(pdf, @"/Type /Page\b").Count);
            Assert.Single(Regex.Matches(pdf, @"/Subtype /Form\b"));

            var formObject = Regex.Match(pdf, @"(\d+) 0 obj(?:(?!\d+ 0 obj).)*?/Subtype /Form",
                RegexOptions.Singleline).Groups[1].Value;
            Assert.NotEmpty(formObject);
            Assert.Equal(2, Regex.Matches(pdf, @"/XObject\s*<<\s*/\w+ " + formObject + @" 0 R").Count);
            var placements = Regex.Matches(pdf, @"/\w+ Do\b").Count;
            if (tiled)
                Assert.True(placements > 2, "the border must invoke the form for its separate slices");
            else
                Assert.Equal(2, placements);
        }

        /// <summary>
        /// Same-page sibling of <see cref="AssertOneFormOnTwoPages"/>: asserts a single page contains
        /// exactly one Form XObject invoked <paramref name="placementCount"/> times, for the
        /// separate-elements-sharing-one-source scenario (issue #1173) rather than one element repainted
        /// across pages.
        /// </summary>
        private static void AssertOneFormOnOnePage(string pdf, int placementCount)
        {
            Assert.Single(Regex.Matches(pdf, @"/Type /Page\b"));
            Assert.Single(Regex.Matches(pdf, @"/Subtype /Form\b"));

            var formObject = Regex.Match(pdf, @"(\d+) 0 obj(?:(?!\d+ 0 obj).)*?/Subtype /Form",
                RegexOptions.Singleline).Groups[1].Value;
            Assert.NotEmpty(formObject);
            Assert.Equal(placementCount, Regex.Matches(pdf, @"/\w+ Do\b").Count);
        }

        [Fact]
        public async Task TwoImgElementsWithSameSvgSource_ShareOneFormOnOnePage()
        {
            var url = SvgDataUri();
            var pdf = await Render($"""
                <img src="{url}" style="width:40pt;height:40pt"/>
                <img src="{url}" style="width:40pt;height:40pt"/>
                """);

            AssertOneFormOnOnePage(pdf, placementCount: 2);
        }

        [Fact]
        public async Task ImgAndBackgroundImage_SameSvgSource_ShareOneFormOnOnePage()
        {
            var url = SvgDataUri();
            var pdf = await Render($"""
                <img src="{url}" style="width:40pt;height:40pt"/>
                <div style="width:40pt;height:40pt;background-image:url('{url}');
                            background-size:40pt 40pt;background-repeat:no-repeat"></div>
                """);

            AssertOneFormOnOnePage(pdf, placementCount: 2);
        }

        [Fact]
        public async Task TwoImgElementsWithDifferentSvgSources_KeepDistinctForms()
        {
            var pdf = await Render($"""
                <img src="{SvgDataUri("#c0392b")}" style="width:40pt;height:40pt"/>
                <img src="{SvgDataUri("#2980b9")}" style="width:40pt;height:40pt"/>
                """);

            Assert.Equal(2, Regex.Matches(pdf, @"/Subtype /Form\b").Count);
            Assert.Equal(2, Regex.Matches(pdf, @"/\w+ Do\b").Count);
            Assert.Contains("0.753", pdf);
            Assert.Contains("0.161", pdf);
        }

        [Fact]
        public async Task FixedInlineSvg_ReusesOneFormAcrossPages()
        {
            var pdf = await Render("""
                <svg style="position:fixed;top:0;left:0" viewBox="0 0 20 20" width="20" height="20">
                  <rect width="20" height="20" fill="#c0392b"/>
                </svg>
                <div>first page</div><div style="break-before:page">second page</div>
                """);

            AssertOneFormOnTwoPages(pdf);
            Assert.Single(Regex.Matches(pdf, @"0\.753\d* 0\.224\d* 0\.169\d* rg"));
        }

        [Fact]
        public async Task FixedImgSvg_ReusesOneFormAcrossPages()
        {
            var pdf = await Render($"""
                <img src="{SvgDataUri()}" style="position:fixed;top:0;left:0;width:40pt;height:40pt"/>
                <div>first page</div><div style="break-before:page">second page</div>
                """);

            AssertOneFormOnTwoPages(pdf);
        }

        [Fact]
        public async Task FixedSvgBackground_ReusesOneFormAcrossPages()
        {
            var url = SvgDataUri();
            var pdf = await Render($"""
                <div style="position:fixed;top:0;left:0;width:40pt;height:40pt;
                            background-image:url('{url}');background-size:40pt 40pt;
                            background-repeat:no-repeat"></div>
                <div>first page</div><div style="break-before:page">second page</div>
                """);

            AssertOneFormOnTwoPages(pdf);
            Assert.Contains("/BBox [0 0 40 40]", pdf);
        }

        [Fact]
        public async Task FixedSvgBorderImage_ReusesOneFormAcrossPages()
        {
            var url = SvgDataUri();
            var pdf = await Render($"""
                <div style="position:fixed;top:0;left:0;width:40pt;height:40pt;
                            border:10pt solid transparent;border-image-source:url('{url}');
                            border-image-slice:5;border-image-repeat:repeat"></div>
                <div>first page</div><div style="break-before:page">second page</div>
                """);

            AssertOneFormOnTwoPages(pdf, tiled: true);
            // The SVG's own intrinsic 20x20 CSS px, in points - CSS Images 3's default sizing algorithm
            // uses an intrinsic size verbatim, so the artwork is sliced at its own scale rather than
            // stretched over the 60pt border-image area first.
            Assert.Contains("/BBox [0 0 15 15]", pdf);
        }

        [Fact]
        public async Task SeparateInlineSvgCurrentColors_KeepDistinctForms()
        {
            var pdf = await Render("""
                <div style="color:red"><svg viewBox="0 0 20 20" width="20" height="20">
                  <rect width="20" height="20" fill="currentColor"/></svg></div>
                <div style="color:blue"><svg viewBox="0 0 20 20" width="20" height="20">
                  <rect width="20" height="20" fill="currentColor"/></svg></div>
                """);

            Assert.Equal(2, Regex.Matches(pdf, @"/Subtype /Form\b").Count);
            Assert.Equal(2, Regex.Matches(pdf, @"/\w+ Do\b").Count);
            Assert.Contains("1 0 0 rg", pdf);
            Assert.Contains("0 0 1 rg", pdf);
        }

        [Fact]
        public async Task SubPointInlineSvg_PaintsDirectlyWhenFormWouldBeTooSmall()
        {
            var pdf = await Render("""
                <svg viewBox="0 0 1 1" width="1" height="1">
                  <rect width="1" height="1" fill="#c0392b"/>
                </svg>
                """);

            Assert.DoesNotContain("/Subtype /Form", pdf);
            Assert.Contains("0.753", pdf);
        }
    }
}
