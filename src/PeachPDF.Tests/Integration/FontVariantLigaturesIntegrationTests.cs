using PeachPDF.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for real GSUB ligature rendering (issue #176, slice 1): PeachPDF now
    /// applies the <c>liga</c>/<c>clig</c>/<c>rlig</c> features (<see cref="GsubShaper"/>) instead
    /// of mapping every codepoint 1:1 via <c>cmap</c>. Per CLAUDE.md's testing conventions, a
    /// content-stream-substring check alone isn't sufficient for this kind of change (the exact gap
    /// that let a broken feature ship before), so this covers both the actual
    /// <c>RGraphics.DrawString</c> call sequence (which <see cref="LigatureFeatures"/> reaches paint)
    /// and a full render's embedded ToUnicode map (does the merged ligature glyph still extract back
    /// to its original text).
    /// </summary>
    public class FontVariantLigaturesIntegrationTests
    {
        [Fact]
        public async Task FontVariantLigatures_None_ResolvesToDisabledFeatures_DefaultResolvesToEnabled()
        {
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-ligatures:none\">ff</span><span id=\"b\">ff</span>");

            var boxNone = FindWordsBox(container.Root!, "a");
            var boxDefault = FindWordsBox(container.Root!, "b");

            Assert.Equal(LigatureFeatures.None, boxNone.ActualFontVariantLigatures);
            Assert.Equal(LigatureFeatures.Default, boxDefault.ActualFontVariantLigatures);
        }

        [Fact]
        public async Task FontVariantLigatures_None_MeasuresWiderThanDefault_ForALigatingRun()
        {
            // Source Sans 3's "f_f" ligature glyph is narrower than two separate 'f' advances (see
            // GetTextOutlineTests), so disabling ligatures must measure "ff" as wider, not just parse
            // differently - this is what proves DerivedStyle.ActualFontVariantLigatures actually
            // reaches CssBox.MeasureWordsSize, not just CssBox.FontVariantLigatures's raw string.
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-ligatures:none\">ff</span><br><span id=\"b\">ff</span>");

            var boxNone = FindWordsBox(container.Root!, "a");
            var boxDefault = FindWordsBox(container.Root!, "b");

            Assert.True(boxNone.Words[0].Width > boxDefault.Words[0].Width,
                $"expected font-variant-ligatures:none to measure wider; none={boxNone.Words[0].Width}, default={boxDefault.Words[0].Width}");
        }

        [Fact]
        public async Task FontVariantLigatures_PaintsWithTheResolvedFeatures_ForBothBoxes()
        {
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-ligatures:none\">ff</span><span id=\"b\">ff</span>");
            var boxA = FindById(container.Root!, "a")!;
            var boxB = FindById(container.Root!, "b")!;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, boxA, recorder);
            FragmentPaintHarness.PaintBox(container, boxB, recorder);

            Assert.Equal(2, recorder.DrawStringCalls.Count);
            Assert.Equal(LigatureFeatures.None, recorder.DrawStringCalls[0].LigatureFeatures);
            Assert.Equal(LigatureFeatures.Default, recorder.DrawStringCalls[1].LigatureFeatures);
        }

        [Fact]
        public async Task Ligature_RendersEmbedsAndRoundTripsToUnicode()
        {
            // Mirrors Format12CmapAstralTests.Emoji_AstralCodepoint_RendersEmbedsAndRoundTripsToUnicode's
            // pattern: render for real, then inspect the embedded PDF's ToUnicode CMap directly.
            var b64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));
            var html = $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{b64}') format('truetype'); }}
body {{ font-family: 'SS3'; font-size: 40pt; }}
</style></head><body>ff</body></html>";

            var generator = new PdfGenerator();
            var doc = await generator.GeneratePdf(html, PageSize.A4);
            doc.PdfDocument.Options.CompressContentStreams = false; // keep the ToUnicode stream readable

            var ms = new MemoryStream();
            doc.Save(ms);
            var pdf = Encoding.Latin1.GetString(ms.ToArray());

            Assert.Contains("/FontFile2", pdf);
            // The "ff" ligature glyph's ToUnicode bfrange destination must carry BOTH characters back
            // (0066 0066 in hex) - not a single 'f', and not nothing.
            Assert.Contains("00660066", pdf);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        // The UA default font has no GSUB `liga` feature that ligates "ff", so these tests load
        // Source Sans 3 explicitly (same font GetTextOutlineTests/ShapingCharacterizationTests use)
        // rather than relying on whatever the platform default happens to be.
        private static readonly string FontFaceBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Ttf));

        private static string Wrap(string body) =>
            $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'SS3'; src: url('data:font/truetype;base64,{FontFaceBase64}') format('truetype'); }}
body {{ font-family: 'SS3'; width: 400px; }}
</style></head><body>{body}</body></html>";

        private static async Task<HtmlContainerInt> LayoutHtml(string body)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(Wrap(body), null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }

        private static CssBox FindWordsBox(CssBox root, string id)
        {
            var element = FindById(root, id)!;
            if (element.Words.Count > 0) return element;

            var wordsChild = element.Boxes.FirstOrDefault(b => b.Words.Count > 0);
            Assert.NotNull(wordsChild);
            return wordsChild!;
        }

        private sealed class RecordingGraphics : RGraphics
        {
            public List<(string Text, LigatureFeatures LigatureFeatures)> DrawStringCalls { get; } = [];

            public RecordingGraphics(RAdapter adapter)
                : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null, LigatureFeatures ligatureFeatures = LigatureFeatures.Default)
                => DrawStringCalls.Add((str, ligatureFeatures));

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, LigatureFeatures ligatureFeatures = LigatureFeatures.Default) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font, LigatureFeatures ligatureFeatures = LigatureFeatures.Default) => new(0, 12);
            public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth)
            {
                charFit = str?.Length ?? 0;
                charFitWidth = 0;
            }
            public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) { }
            public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
            public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) { }
            public override void DrawImage(RImage image, RRect destRect, RRect srcRect) { }
            public override void DrawImage(RImage image, RRect destRect) { }
            public override void DrawPath(RPen pen, RGraphicsPath path) { }
            public override void DrawPath(RBrush brush, RGraphicsPath path) { }
            public override void DrawPolygon(RBrush brush, RPoint[] points) { }
            public override void Dispose() { }
        }
    }
}
