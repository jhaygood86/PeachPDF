using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Direct unit tests for <see cref="MarginBoxRenderer.BuildFont"/> — the font-resolution step for
    /// @page margin-box text (<c>@top-left</c>, <c>@bottom-right</c>, etc.). Exercised directly (rather
    /// than through full HTML→PDF generation + rendered-text extraction) per this repo's testing
    /// conventions: PeachPDF embeds subsetted fonts, so decoded PDF content-stream <c>Tj</c> operands are
    /// glyph indices, not literal ASCII text. These are the direct regression tests for the bug where
    /// margin-box text rendered in the wrong font: no inheritance from the winning <c>@page</c> rule's own
    /// <c>font</c> declarations (fixed via <c>pageStyle</c>), no font-stack/generic-family fallback (a raw
    /// <c>XFont</c> was built directly instead of going through <see cref="FontFamilyResolver"/>), and no
    /// keyword/relative font-size support (fixed via <see cref="FontSizeResolver"/>).
    /// </summary>
    public class MarginBoxRendererFontTests
    {
        [Fact]
        public void OwnFontFamily_WinsOverPageContext()
        {
            var marginStyle = ParseDeclarations("font-family: monospace;");
            var pageStyle = ParseDeclarations("font-family: serif;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, pageStyle, adapter);
            var expected = adapter.GetFont("monospace", CssConstants.FontSize, RFontStyle.Regular) as FontAdapter;

            Assert.Equal(expected!.Font.Name, font.Name);
        }

        [Fact]
        public void NoOwnFontFamily_InheritsFromPageContext()
        {
            // Mirrors the real dictionary CSS: the margin box's own block never sets font-family - only
            // the enclosing @page rule does. Direct regression test for the missing inheritance path.
            var marginStyle = ParseDeclarations("content: \"x\";");
            var pageStyle = ParseDeclarations("font-family: monospace;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, pageStyle, adapter);
            var expected = adapter.GetFont("monospace", CssConstants.FontSize, RFontStyle.Regular) as FontAdapter;

            Assert.Equal(expected!.Font.Name, font.Name);
        }

        [Fact]
        public void NeitherSet_FallsBackToCssConstantsDefaults()
        {
            var marginStyle = ParseDeclarations("content: \"x\";");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);
            var expected = adapter.GetFont(CssConstants.DefaultFont, CssConstants.FontSize, RFontStyle.Regular) as FontAdapter;

            Assert.Equal(expected!.Font.Name, font.Name);
            Assert.Equal(CssConstants.FontSize, font.Size, 3);
        }

        [Fact]
        public void CommaSeparatedFallback_SkipsUnresolvableFirstFamily()
        {
            var marginStyle = ParseDeclarations("font-family: \"NonexistentFontXYZ\", monospace;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);
            var expected = adapter.GetFont("monospace", CssConstants.FontSize, RFontStyle.Regular) as FontAdapter;

            Assert.Equal(expected!.Font.Name, font.Name);
        }

        [Fact]
        public void FontWeightBold_ResolvesToBoldFont()
        {
            var marginStyle = ParseDeclarations("font-weight: bold;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);

            Assert.True(font.Bold);
        }

        [Fact]
        public void FontWeightNumeric700_ResolvesToBoldFont()
        {
            var marginStyle = ParseDeclarations("font-weight: 700;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);

            Assert.True(font.Bold);
        }

        [Fact]
        public void FontStyleItalic_ResolvesToItalicFont()
        {
            var marginStyle = ParseDeclarations("font-style: italic;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);

            Assert.True(font.Italic);
        }

        [Fact]
        public void KeywordFontSize_ResolvesViaSharedTable()
        {
            var marginStyle = ParseDeclarations("font-size: large;");
            var adapter = NewAdapter();

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);

            Assert.Equal(CssConstants.FontSize + 2, font.Size, 3);
        }

        [Fact]
        public void PixelsPerPointScaling_StillYieldsCssSpecifiedPointSize()
        {
            var marginStyle = ParseDeclarations("font-size: 20pt;");
            var adapter = NewAdapter();
            adapter.PixelsPerPoint = 1.5;

            var font = MarginBoxRenderer.BuildFont(marginStyle, null, adapter);

            Assert.Equal(20.0, font.Size, 3);
        }

        [Fact]
        public void AbsolutePointSize_AgreesWithDomParserConversion()
        {
            // Locks in the numeric-convention assumption FontSizeResolver's fallback path relies on:
            // CssValueParser.ParseLength and DomParser.ParseLengthToPdfPoints must agree for a bare `pt`
            // value, since BuildFont tries the latter first and only falls back to the former for
            // keywords/relative units it can't handle.
            var direct = PeachPDF.Html.Core.Parse.DomParser.ParseLengthToPdfPoints("14pt");
            var viaSharedTable = FontSizeResolver.Resolve("14pt", CssConstants.FontSize, CssConstants.FontSize);

            Assert.Equal(direct, viaSharedTable);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static PdfSharpAdapter NewAdapter()
        {
            var adapter = new PdfSharpAdapter { PixelsPerPoint = 1.0 };
            return adapter;
        }

        private static StyleDeclaration ParseDeclarations(string css) =>
            new StylesheetParser().Parse($"@page {{ @top-left {{ {css} }} }}")
                .Rules.OfType<PageRule>().Single().Margins.Single().Style;
    }
}
