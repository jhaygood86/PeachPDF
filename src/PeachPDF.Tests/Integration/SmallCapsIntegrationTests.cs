using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Regression coverage for real <c>font-variant: small-caps</c> rendering. Previously this property
    /// was parsed, cascaded, and inherited (<see cref="CssBoxProperties.FontVariant"/>) but never
    /// consumed anywhere in layout or paint — a full-tree grep found zero non-storage read sites, so the
    /// property was a complete no-op for any spelling, including the standard keyword. PeachPDF has no
    /// OpenType shaping engine to do real <c>smcp</c>/<c>c2sc</c> glyph substitution, so small-caps is
    /// synthesized: originally-lowercase runs are upper-cased and measured/painted at a reduced size
    /// (<see cref="CssBoxProperties.ActualSmallCapsFont"/>) relative to the rest of the word.
    ///
    /// Per CLAUDE.md's testing conventions: a parser-only test isn't sufficient on its own for a paint
    /// feature (this exact class of gap — a value that parses correctly but never renders — previously
    /// let a fully-broken feature ship for months), so this file covers both the real layout output
    /// (CssBox/CssRectWord properties after layout) and the actual paint call sequence.
    /// </summary>
    public class SmallCapsIntegrationTests
    {
        [Fact]
        public async Task SmallCaps_SplitsWordIntoCaseRuns()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style=\"font-variant:small-caps\">Hello</b>");
            var box = FindWordsBox(container.Root!, "w");

            // "Hello" -> "H" (already upper) + "ello" (synthesized small-caps run).
            Assert.Equal(2, box.Words.Count);
            Assert.Equal("H", box.Words[0].Text);
            Assert.Equal("ELLO", box.Words[1].Text);
        }

        [Fact]
        public async Task SmallCaps_LowercaseRunIsScaledDown_UppercaseRunIsNot()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style=\"font-variant:small-caps\">Hello</b>");
            var box = FindWordsBox(container.Root!, "w");

            Assert.Equal(1.0, box.Words[0].FontSizeScale); // "H"
            Assert.Equal(CssBoxProperties.SmallCapsFontScale, box.Words[1].FontSizeScale); // "ello" -> "ELLO"
            Assert.True(box.ActualSmallCapsFont.Size < box.ActualFont.Size);
        }

        [Fact]
        public async Task SmallCaps_MeasuredWidthReflectsTheScaledFont()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style=\"font-variant:small-caps\">Hello</b>");
            var box = FindWordsBox(container.Root!, "w");

            // The synthesized run's width must come from actually measuring at the smaller font — not
            // just inheriting the box's normal ActualFont metrics (which would make this test pass even
            // if FontSizeScale were silently ignored at measurement time).
            using var g = MeasureGraphics();
            var expectedWidth = g.MeasureString("ELLO", box.ActualSmallCapsFont).Width;

            Assert.Equal(expectedWidth, box.Words[1].Width, 3);
        }

        [Fact]
        public async Task SmallCaps_FragmentsStayGluedOnOneLine_NoSpuriousWrap()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style=\"font-variant:small-caps\">Hello</b>");
            var box = FindWordsBox(container.Root!, "w");

            Assert.False(box.Words[0].SuppressWrapBefore); // first fragment of a word is a normal break point
            Assert.True(box.Words[1].SuppressWrapBefore); // continuation fragment must never itself wrap

            // Both fragments land on the same line and are laid out left-to-right, contiguous.
            Assert.Equal(box.Words[0].Top, box.Words[1].Top, 3);
            Assert.True(box.Words[1].Left >= box.Words[0].Right - 0.01);
        }

        [Fact]
        public async Task SmallCaps_PreservesOriginalWordSpaceFlagsOnFirstAndLastFragmentOnly()
        {
            var container = await LayoutHtml(
                "<p id=\"p\" style=\"font-variant:small-caps\">One Hello Two</p>");
            var box = FindWordsBox(container.Root!, "p");

            var helloRuns = box.Words
                .SkipWhile(w => w.Text != "H")
                .TakeWhile(w => w.Text is "H" or "ELLO")
                .ToList();

            // Note: HasSpaceBefore is only ever true for the very first word ParseToWords adds to a box
            // (existing, pre-small-caps semantics — inter-word spacing within one text run is carried by
            // the *preceding* word's HasSpaceAfter, not the following word's HasSpaceBefore). "Hello" is
            // the second word in this run, so its original HasSpaceBefore was already false.
            Assert.Equal(2, helloRuns.Count);
            Assert.False(helloRuns[0].HasSpaceBefore);
            Assert.False(helloRuns[0].HasSpaceAfter); // glued to "ELLO"
            Assert.False(helloRuns[1].HasSpaceBefore); // glued fragment, no space of its own
            Assert.True(helloRuns[1].HasSpaceAfter); // followed by " Two"
        }

        [Fact]
        public async Task NoSmallCaps_WordIsNotSplit_Regression()
        {
            var container = await LayoutHtml("<b id=\"w\">Hello</b>");
            var box = FindWordsBox(container.Root!, "w");

            Assert.Single(box.Words);
            Assert.Equal("Hello", box.Words[0].Text);
            Assert.Equal(1.0, box.Words[0].FontSizeScale);
        }

        [Fact]
        public async Task SmallCaps_WordWithNoLowercaseLetters_IsNotSplit()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style=\"font-variant:small-caps\">ABC</b>");
            var box = FindWordsBox(container.Root!, "w");

            Assert.Single(box.Words);
            Assert.Equal("ABC", box.Words[0].Text);
            Assert.Equal(1.0, box.Words[0].FontSizeScale);
        }

        // ── Painting: actual RGraphics.DrawString call sequence ─────────────────

        [Fact]
        public async Task SmallCaps_PaintsMultipleDrawStringCalls_WithDifferentFontSizes_InOrder()
        {
            var container = await LayoutHtml(
                "<b id=\"w\" style=\"font-variant:small-caps\">Hello</b>");
            var elementBox = FindById(container.Root!, "w")!;

            var recorder = new RecordingGraphics(new PdfSharpAdapter());
            await elementBox.Paint(recorder);

            Assert.Equal(2, recorder.DrawStringCalls.Count);
            Assert.Equal("H", recorder.DrawStringCalls[0].Text);
            Assert.Equal("ELLO", recorder.DrawStringCalls[1].Text);

            // Different fonts (small-caps run uses the smaller derived font).
            Assert.True(recorder.DrawStringCalls[0].Font.Size > recorder.DrawStringCalls[1].Font.Size);

            // Left-to-right paint order.
            Assert.True(recorder.DrawStringCalls[1].Point.X > recorder.DrawStringCalls[0].Point.X);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body style=\"width:400px\">{body}</body></html>";

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

        /// <summary>
        /// Text nodes get their own anonymous child <see cref="CssBox"/> (see
        /// <c>DomParser.CorrectTextBoxes</c>), so an element like <c>&lt;b id="w"&gt;Hello&lt;/b&gt;</c>'s
        /// own box has an empty <see cref="CssBox.Words"/> — the words live on its single anonymous text
        /// child instead. This walks down to that box.
        /// </summary>
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
            public List<(string Text, RFont Font, RPoint Point)> DrawStringCalls { get; } = [];

            public RecordingGraphics(RAdapter adapter)
                : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue)) { }

            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null)
                => DrawStringCalls.Add((str, font, point));

            public override void PushTransform(RMatrix matrix) { }
            public override void PopTransform() { }
            public override void PushClip(RRect rect) => _clipStack.Push(rect);
            public override void PushClip(RGraphicsPath path) => _clipStack.Push(_clipStack.Peek());
            public override void PopClip() { if (_clipStack.Count > 1) _clipStack.Pop(); }
            public override void PushClipExclude(RRect rect) { }
            public override object SetAntiAliasSmoothingMode() => new object();
            public override void ReturnPreviousSmoothingMode(object? prevMode) { }
            public override RGraphicsPath GetGraphicsPath() => null!;

            public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0) => null;
            public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height) => null;
            public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) { }
            public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity) { }
            public override void BeginMarkedContent(string structureType, int mcid) { }
            public override void EndMarkedContent() { }
            public override void BeginArtifact() { }
            public override RSize MeasureString(string str, RFont font) => new(0, 12);
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
