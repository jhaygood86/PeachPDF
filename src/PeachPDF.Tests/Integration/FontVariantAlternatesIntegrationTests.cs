using PeachPDF.Text;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using PeachPDF.Tests.TestSupport;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage for CSS Fonts Module 4's <c>font-variant-alternates</c> property and
    /// <c>@font-feature-values</c> at-rule (issue #1281) against a real font with genuine numbered
    /// stylistic-set GSUB data (<see cref="BundledFonts.Recursive"/> - <c>ss01</c>/<c>ss02</c>, real
    /// GSUB Single Substitution). Per CLAUDE.md's testing conventions, this checks the actual
    /// <c>TextShapingFeatures</c> reaching <c>RGraphics.DrawString</c> (not just that the declaration
    /// parses), and the showcase rasterization (see TestHarness) is what proves the alternate glyph is
    /// actually drawn.
    /// </summary>
    public class FontVariantAlternatesIntegrationTests
    {
        private const string FontFeatureValuesCss = "@font-feature-values Recursive { @styleset { simple-a: 1; simple-g: 2; } }";

        [Fact]
        public async Task Styleset_ResolvesToRegisteredFeatureTag()
        {
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-alternates:styleset(simple-a)\">cat</span>");
            var box = FindWordsBox(container.Root!, "a");

            Assert.Equal([("ss01", 1)], box.ActualFontVariantAlternates);
            Assert.Contains(("ss01", 1), box.ActualTextShapingFeatures.ExplicitFeatures!);
        }

        [Fact]
        public async Task Styleset_MultipleNames_ResolvesToMultipleTags()
        {
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-alternates:styleset(simple-a, simple-g)\">cat</span>");
            var box = FindWordsBox(container.Root!, "a");

            Assert.Equal([("ss01", 1), ("ss02", 1)], box.ActualFontVariantAlternates);
        }

        [Fact]
        public async Task UnmatchedName_ResolvesToNoFeatures()
        {
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-alternates:styleset(bogus)\">cat</span>");
            var box = FindWordsBox(container.Root!, "a");

            Assert.Empty(box.ActualFontVariantAlternates);
        }

        [Fact]
        public async Task Alternates_TakesPrecedenceOverFeatureSettingsForSameTag()
        {
            // font-feature-settings turns ss01 off; font-variant-alternates's styleset(simple-a) still
            // turns it on - CSS Fonts 4 §6.8: font-variant-alternates wins over font-feature-settings
            // for a tag both touch.
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-feature-settings:'ss01' off; font-variant-alternates:styleset(simple-a)\">cat</span>");
            var box = FindWordsBox(container.Root!, "a");

            var features = box.ActualTextShapingFeatures.ExplicitFeatures!;
            Assert.Contains(("ss01", 1), features);
        }

        [Fact]
        public async Task Alternates_PaintsWithTheResolvedFeatures()
        {
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-alternates:styleset(simple-a)\">cat</span><span id=\"b\">cat</span>");
            var boxA = FindById(container.Root!, "a")!;
            var boxB = FindById(container.Root!, "b")!;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            FragmentPaintHarness.PaintBox(container, boxA, recorder);
            FragmentPaintHarness.PaintBox(container, boxB, recorder);

            Assert.Equal(2, recorder.DrawStringCalls.Count);
            Assert.Contains(("ss01", 1), recorder.DrawStringCalls[0].Features.ExplicitFeatures!);
            Assert.True(recorder.DrawStringCalls[1].Features.ExplicitFeatures is null or []);
        }

        [Fact]
        public async Task Styleset_SelectsADifferentGlyphThanDefault()
        {
            // ss01 is a real GSUB Single Substitution (a -> a.simple) in the bundled Recursive subset -
            // proves the feature reaches real glyph selection, not just the feature-tag plumbing. The
            // showcase (TestHarness) rasterizes this same comparison for a visual proof per CLAUDE.md's
            // testing conventions; this asserts on the actual decoded outline geometry.
            var container = await LayoutHtml(
                "<span id=\"a\" style=\"font-variant-alternates:styleset(simple-a)\">a</span><span id=\"b\">a</span>");
            var boxA = FindWordsBox(container.Root!, "a");
            var boxB = FindWordsBox(container.Root!, "b");

            using var g = MeasureGraphics();
            var font = boxA.ActualFont;
            using var outlineWithAlternate = g.GetTextOutline("a", font, new RPoint(0, 100), features: boxA.ActualTextShapingFeatures)!;
            using var outlineDefault = g.GetTextOutline("a", font, new RPoint(0, 100), features: boxB.ActualTextShapingFeatures)!;

            Assert.NotNull(outlineWithAlternate);
            Assert.NotNull(outlineDefault);
            Assert.NotEqual(Points(outlineDefault), Points(outlineWithAlternate));
        }

        private static XPoint[] Points(RGraphicsPath path) =>
            ((GraphicsPathAdapter)path).GraphicsPath._corePath.PathPoints;

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static readonly string FontFaceBase64 = Convert.ToBase64String(File.ReadAllBytes(BundledFonts.Recursive));

        private static string Wrap(string body) =>
            $@"<!DOCTYPE html><html><head><style>
@font-face {{ font-family: 'Recursive'; src: url('data:font/truetype;base64,{FontFaceBase64}') format('truetype'); }}
body {{ font-family: 'Recursive'; width: 400px; }}
{FontFeatureValuesCss}
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

        private static GraphicsAdapter MeasureGraphics()
        {
            var adapter = new PdfSharpAdapter();
            var size = new XSize(595, 842);
            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            return new GraphicsAdapter(adapter, measure, 1.0);
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
            public List<(string Text, TextShapingFeatures Features)> DrawStringCalls { get; } = [];

            public RecordingGraphics(RAdapter adapter)
                : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, TextShapingFeatures? features = null)
                => DrawStringCalls.Add((str, features ?? TextShapingFeatures.Default));
            public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, RFont font, RColor color) { }

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushBlendMode(RBlendMode mode) { }
            public override void PopBlendMode() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, TextShapingFeatures? features = null) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal) { }
            public override void DrawImageWithColorMatrix(RImage image, RRect destRect, ColorMatrix matrix) { }
            public override void DrawImageAlphaMasked(RImage image, RImage maskImage, RRect destRect, bool invert = false) { }
            public override void DrawImageBlendedOver(RImage top, RImage bottom, RRect destRect, RBlendMode blendMode) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override void BeginVariableText() { }
            public override void EndVariableText() { }
            public override RSize MeasureString(string str, RFont font, TextShapingFeatures? features = null) => new(0, 12);
            public override int CountShapedGlyphs(string str, RFont font, TextShapingFeatures? features = null) => str?.Length ?? 0;
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
