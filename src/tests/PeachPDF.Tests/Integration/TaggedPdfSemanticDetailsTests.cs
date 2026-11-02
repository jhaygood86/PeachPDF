using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Structure;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What tagged PDF takes from the document's accessibility tree: the alternative text of pictures and formulas from their names, the language an
    /// element sets, and the scope and spans of table cells.
    /// </summary>
    public class TaggedPdfSemanticDetailsTests
    {
        const string TinyGif = "data:image/gif;base64,R0lGODlhAQABAAAAACw=";

        static async Task<PdfFile> RenderAsync(string body)
        {
            var generator = new PdfGenerator();
            await using (var font = File.OpenRead(BundledFonts.Math))
                await generator.AddFontFromStream(font);

            generator.AddFontFamilyMapping("TestMath", "STIX Two Math");
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await generator.GeneratePdf($"<html><head><style>math {{ font-family: TestMath; }}</style></head><body>{body}</body></html>", config);
            return result.Inspect();
        }

        static IEnumerable<StructElem> All(StructElem element)
        {
            yield return element;
            foreach (var kid in element.Kids)
            {
                foreach (var below in All(kid))
                    yield return below;
            }
        }

        static IEnumerable<StructElem> Elements(PdfFile file) => file.StructureRoot.SelectMany(All);

        [Fact]
        public async Task AnSvgGetsItsTitleAsAlternativeText()
        {
            var file = await RenderAsync("<svg width=40 height=40 viewBox='0 0 40 40'><title>A red square</title><rect width=30 height=30 fill=red /></svg>");

            Assert.Equal("A red square", Elements(file).Single(e => e.StructureType == "/Figure").AlternateText);
        }

        [Fact]
        public async Task AnAriaLabelIsTheAlternativeTextOfAnImageThatHasNoAlt()
        {
            var file = await RenderAsync($"<img src='{TinyGif}' aria-label='A labelled dot'><svg width=10 height=10 aria-label='Labelled svg'><rect width=5 height=5 /></svg>");

            Assert.Equal(["A labelled dot", "Labelled svg"], Elements(file).Where(e => e.StructureType == "/Figure").Select(e => e.AlternateText).ToArray());
        }

        [Fact]
        public async Task AnAltAttributeStillWins()
        {
            var file = await RenderAsync($"<img src='{TinyGif}' alt='The alt' aria-label='The label'>");

            Assert.Equal("The alt", Elements(file).Single(e => e.StructureType == "/Figure").AlternateText);
        }

        [Fact]
        public async Task AlternativeTextInAnyScriptIsWritten()
        {
            // A raw PDF string holds only ASCII: accented, right-to-left and CJK text used to make tagging throw.
            var file = await RenderAsync($"<img src='{TinyGif}' alt='Café'><img src='{TinyGif}' alt='صورة'><img src='{TinyGif}' alt='猫が寝ている'><math><mtext>مرحبا</mtext><mo>+</mo><mi>x</mi></math>");

            Assert.Equal(["Café", "صورة", "猫が寝ている"], Elements(file).Where(e => e.StructureType == "/Figure").Select(e => e.AlternateText).ToArray());
            Assert.Equal("مرحبا plus x", Elements(file).Single(e => e.StructureType == "/Formula").AlternateText);
        }

        [Fact]
        public async Task AFormulaIsSaidInEnglishInItsAlternativeText_UnlessTheAuthorGaveOne()
        {
            var file = await RenderAsync("<math><msup><mi>x</mi><mn>2</mn></msup><mo>+</mo><mn>1</mn></math><math alttext='Author reading'><mi>y</mi></math>");

            Assert.Equal(["x squared plus 1", "Author reading"], Elements(file).Where(e => e.StructureType == "/Formula").Select(e => e.AlternateText).ToArray());
        }

        [Fact]
        public async Task AnElementThatSetsALanguageGivesItToItsStructureElement()
        {
            var file = await RenderAsync("<p>Plain</p><p lang='fr'>Bonjour</p><div lang='de'><p>Hallo</p></div>");

            // The structure tree follows the order things are painted in, so compare without depending on it.
            var languages = Elements(file).Where(e => e.StructureType is "/P" or "/Div").Select(e => e.Dict.Text("/Lang") ?? "none").OrderBy(l => l, StringComparer.Ordinal).ToArray();
            Assert.Equal(["de", "fr", "none", "none"], languages);
        }

        [Fact]
        public async Task TableCellsCarryTheirScopeAndSpans()
        {
            var file = await RenderAsync("<table><tr><th>Col</th><th scope=row>Row</th></tr><tr><td colspan=2>wide</td></tr><tr><td rowspan=2>tall</td><td>x</td></tr><tr><td>y</td></tr></table>");

            static PdfDict? Table(StructElem e) => e.Dict.Dict("/A") is { } a && a.Name("/O") == "/Table" ? a : null;

            var heads = Elements(file).Where(e => e.StructureType == "/TH").ToArray();
            Assert.Equal(["/Column", "/Row"], heads.Select(h => Table(h)!.Name("/Scope")).ToArray());

            var cells = Elements(file).Where(e => e.StructureType == "/TD").ToArray();
            Assert.Equal(2, Table(cells[0])!.Number("/ColSpan"));
            Assert.Null(Table(cells[0])!.Number("/RowSpan"));
            Assert.Equal(2, Table(cells[1])!.Number("/RowSpan"));
            Assert.Null(Table(cells[2]));
        }

        [Fact]
        public async Task ADocumentWithNoSuchDetailsIsTaggedAsBefore()
        {
            var file = await RenderAsync("<h1>Title</h1><p>Body</p><table><tr><td>a</td></tr></table>");

            var types = Elements(file).Select(e => e.StructureType).ToArray();
            Assert.All(new[] { "/Document", "/H1", "/P", "/Table", "/TR", "/TD" }, expected => Assert.Contains(expected, types));
            Assert.All(Elements(file), e => Assert.Null(e.Dict.Text("/Lang")));
            Assert.Null(Elements(file).Single(e => e.StructureType == "/TD").Dict.Dict("/A"));
        }
    }
}
