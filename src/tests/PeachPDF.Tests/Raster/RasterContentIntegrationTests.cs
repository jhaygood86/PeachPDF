using PeachPDF.Tests.TestSupport;
using System.Text.RegularExpressions;

namespace PeachPDF.Tests.Raster
{
    /// <summary>
    /// Complex content rendered through the raster path end to end. The near-identity filter
    /// (<c>saturate(1.0001)</c>) forces the bitmap route without visibly changing anything, so the same document can be
    /// compared against its vector render; the visual comparison was done once with two rasterizers, and these tests keep
    /// every kind of content that path can meet from silently throwing or dropping out.
    /// </summary>
    public class RasterContentIntegrationTests
    {
        private static string Png()
        {
            var bytes = RasterPngFixture.MakeRgbaPngBytes(8, 8, (x, y) => ((x + y) % 2 == 0 ? (byte)230 : (byte)40, (byte)(x * 30), (byte)(y * 30), (byte)255));
            return "data:image/png;base64," + Convert.ToBase64String(bytes);
        }

        private static string KitchenSink(string filter) => $$"""
            <div style="{{filter}}">
            <style>
              .row { display:flex; gap:12px; margin-bottom:12px; font: 11px Arial, sans-serif }
              .card { width:120px; height:90px; border:3px solid #234; border-radius:12px; padding:6px; box-sizing:border-box;
                      background: linear-gradient(135deg,#f80,#08f); color:#fff; overflow:hidden }
              .g2 { background: radial-gradient(circle at 30% 30%, #fff, #c00 60%, #300) }
              .g3 { background: conic-gradient(from 20deg, #f00, #ff0, #0f0, #0ff, #00f, #f0f, #f00) }
              .g4 { background: repeating-linear-gradient(45deg,#333 0 6px,#ddd 6px 12px) }
              .b1 { border-style:dashed; border-color:#a00; background:#fee }
              .b2 { border-style:double; border-width:6px; background:#efe; color:#040 }
              .sh { box-shadow: 4px 4px 0 #888, inset 0 0 10px #48c; background:#fff; color:#000 }
              .tr { transform: rotate(8deg); background:#9cf; color:#013 }
              .img { background:url({{Png()}}) 0 0/24px 24px repeat; }
              table { border-collapse:collapse } td { border:1px solid #666; padding:3px 6px }
            </style>
            <div class="row">
              <div class="card">Linear gradient<br><b>bold</b> <i>italic</i> <u>under</u></div>
              <div class="card g2">Radial</div>
              <div class="card g3">Conic</div>
              <div class="card g4">Repeat</div>
            </div>
            <div class="row">
              <div class="card b1">Dashed border</div>
              <div class="card b2">Double border</div>
              <div class="card sh">Shadows</div>
              <div class="card tr">Rotated</div>
            </div>
            <div class="row">
              <div class="card img">Tiled image</div>
              <img src="{{Png()}}" width="64" height="64" style="border-radius:8px">
              <table><tr><td>Cell A</td><td>Cell B</td></tr><tr><td>1</td><td>2</td></tr></table>
              <svg width="150" height="90" viewBox="0 0 150 90">
                <defs>
                  <linearGradient id="lg"><stop offset="0" stop-color="#f00"/><stop offset="1" stop-color="#00f" stop-opacity=".4"/></linearGradient>
                  <pattern id="pt" width="10" height="10" patternUnits="userSpaceOnUse"><circle cx="5" cy="5" r="3" fill="#080"/></pattern>
                  <mask id="mk"><rect width="150" height="90" fill="#fff"/><circle cx="110" cy="45" r="25" fill="#000"/></mask>
                  <clipPath id="cp"><rect x="5" y="5" width="60" height="40" rx="8"/></clipPath>
                </defs>
                <rect x="5" y="5" width="60" height="40" fill="url(#lg)" clip-path="url(#cp)"/>
                <rect x="70" y="5" width="75" height="40" fill="url(#pt)" stroke="#333" stroke-dasharray="4 2"/>
                <rect x="5" y="50" width="140" height="35" fill="#fc0" mask="url(#mk)"/>
                <text x="10" y="72" font-size="14" fill="#003" stroke="#fff" stroke-width=".5">SVG text</text>
                <path d="M10 45 C 40 10, 80 80, 140 30" fill="none" stroke="#a0a" stroke-width="3" stroke-linecap="round"/>
              </svg>
            </div>
            <p style="font:13px Georgia,serif;letter-spacing:1px">Letter-spaced paragraph with <span style="color:#c00">colour</span> and <span style="text-decoration:line-through">strike</span>.</p>
            </div>
            """;

        private static PdfGenerateConfig Config() => new()
        {
            PageSize = PageSize.A4,
            CompressContentStreams = false,
            MarginLeft = 20,
            MarginTop = 20,
            MarginRight = 20,
            MarginBottom = 20,
        };

        [Fact]
        public async Task EveryKindOfContent_RendersThroughTheRasterPath_AsOneBitmap()
        {
            var pdf = await PdfObjectReader.GeneratePdf($"<html><body>{KitchenSink("filter:saturate(1.0001)")}</body></html>", Config());

            var placed = Regex.Matches(pdf, @"q\s+[-\d.]+\s+0\s+0\s+[-\d.]+\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q");
            Assert.Single(placed);
        }

        [Fact]
        public async Task TheSameContentWithoutTheFilter_HasNoRasterOutput()
        {
            var pdf = await PdfObjectReader.GeneratePdf($"<html><body>{KitchenSink("")}</body></html>", Config());

            // Only the page's own images (the tiled background and the <img>), none of them a raster-backend bitmap.
            var raster = Regex.Matches(pdf, @"q\s+([-\d.]+)\s+0\s+0\s+([-\d.]+)\s+[-\d.]+\s+[-\d.]+\s+cm\s+/I\d+\s+Do\s+Q")
                .Count(m => double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) > 300);
            Assert.Equal(0, raster);
        }

        [Fact]
        public async Task TaggedPdf_AndTransformsInsideAFilteredElement_StillGenerate()
        {
            const string html = """
                <html><body>
                <h1 style="filter:grayscale(1)">Heading <a href="https://example.com">link</a></h1>
                <div style="filter:blur(1pt);transform:rotate(3deg)"><p>Paragraph <b>bold</b></p><ul><li>one</li><li>two</li></ul></div>
                <div style="filter:sepia(1);position:relative"><div style="position:absolute;left:10pt;top:5pt;width:30pt;height:20pt;background:#0a0"></div></div>
                </body></html>
                """;
            var config = Config();
            config.EnableTaggedPdf = true;

            var pdf = await PdfObjectReader.GeneratePdf(html, config);

            Assert.Contains("/StructTreeRoot", pdf);
        }

        [Fact]
        public async Task MathMlAndInlineSvgImages_InsideAFilteredElement_Render()
        {
            const string html = """
                <html><body><div style="filter:hue-rotate(40deg)">
                <math><mfrac><mi>a</mi><mi>b</mi></mfrac></math>
                <img alt="svg" width="40" height="40" src="data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='40' height='40'><circle cx='20' cy='20' r='15' fill='red'/></svg>">
                </div></body></html>
                """;

            var pdf = await PdfObjectReader.GeneratePdf(html, Config());

            Assert.Single(Regex.Matches(pdf, @"/I\d+\s+Do"));
        }

        [Fact]
        public async Task MaxRasterPixels_MustBePositive()
        {
            var config = Config();
            config.MaxRasterPixels = 0;

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => PdfObjectReader.GeneratePdf("<html><body><div style='filter:blur(2px)'>x</div></body></html>", config));
        }
    }
}
