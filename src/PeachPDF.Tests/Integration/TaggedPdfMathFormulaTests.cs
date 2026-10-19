using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Structure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PeachPDF.Tests.TestSupport;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Tagged-PDF coverage for <c>&lt;math&gt;</c>: the default <c>-peachpdf-pdf-tag-type: Formula</c>
    /// mapping, and the PDF 2.0 <c>/AF</c> Associated File attachment of the original MathML source
    /// (ISO 32000-2 §14.13) - see <c>StructureTagBuilder.AttachMathMlSource</c>.
    /// </summary>
    public class TaggedPdfMathFormulaTests
    {
        const string MathStyle = "math { font-family: TestMath; }";

        // Registers the real "STIX Two Math" family directly on the generator (bypassing CSS
        // @font-face - see BundledFonts.RegisterFont) and aliases "TestMath" to it, so every test's
        // markup/assertions keep referring to "TestMath" unchanged.
        static async Task<PdfGenerator> CreateGeneratorWithMathFont()
        {
            var generator = new PdfGenerator();
            using var stream = File.OpenRead(BundledFonts.Math);
            await generator.AddFontFromStream(stream);
            generator.AddFontFamilyMapping("TestMath", "STIX Two Math");
            return generator;
        }

        static async Task<PeachPdfDocument> RenderTagged(string mathHtml, bool pdf20 = false)
        {
            var html = $"<html><head><style>{MathStyle}</style></head><body>{mathHtml}</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                EnableTaggedPdf = true,
                PdfVersion = pdf20 ? PdfVersion.Pdf20 : PdfVersion.Pdf17,
            };
            var generator = await CreateGeneratorWithMathFont();
            return await generator.GeneratePdf(html, config);
        }

        [Fact]
        public async Task Math_DefaultsToFormulaStructureType()
        {
            var result = await RenderTagged("<math><mi>x</mi></math>");

            var documentElement = RootKids(result.Inspect()).Single();
            var formula = Kids(documentElement).Single();

            Assert.Equal("/Formula", formula.StructureType);
        }

        [Fact]
        public async Task Formula_HasAssociatedFile_WithMathMlSourceAndSupplementRelationship()
        {
            var result = await RenderTagged("<math><mi>x</mi><mo>+</mo><mn>1</mn></math>");

            var documentElement = RootKids(result.Inspect()).Single();
            var formula = Kids(documentElement).Single();

            var afArray = formula.Dict.Array("/AF");
            Assert.NotNull(afArray);
            var fileSpec = Assert.IsAssignableFrom<PdfDict>(afArray![0]);

            Assert.Equal("/Supplement", fileSpec.Name("/AFRelationship"));
            Assert.Equal("formula.mml", fileSpec.Text("/F"));
            var embedded = Assert.IsType<PdfStreamObject>(fileSpec.Dict("/EF")!["/F"]);
            Assert.Equal("/application/mathml+xml", embedded.Name("/Subtype"));

            var mathml = Encoding.UTF8.GetString(embedded.Decoded);
            Assert.Contains("<mi>x</mi>", mathml);
            Assert.Contains("<mo>+</mo>", mathml);
            Assert.Contains("<mn>1</mn>", mathml);
            Assert.StartsWith("<math", mathml);
        }

        [Fact]
        public async Task Formula_AssociatedFile_AlsoIndexedInCatalogLevelAf()
        {
            var result = await RenderTagged("<math><mi>x</mi></math>");

            var catalogAf = result.Inspect().Catalog.Array("/AF");
            Assert.NotNull(catalogAf);
            Assert.Single(catalogAf!.Items);
        }

        [Fact]
        public async Task NoTagging_NoAssociatedFileWritten()
        {
            var html = $"<html><head><style>{MathStyle}</style></head>" +
                       "<body><math><mi>x</mi></math></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var generator = await CreateGeneratorWithMathFont();
            var result = await generator.GeneratePdf(html, config);

            Assert.False(result.Inspect().Catalog.ContainsKey("/AF"));
        }

        [Fact]
        public async Task AuthorOverride_RetargetsFormulaToAnotherType_StillAttachesSource()
        {
            var html = "<html><head><style>math { font-family: TestMath; -peachpdf-pdf-tag-type: P; }</style></head>" +
                       "<body><math><mi>x</mi></math></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var generator = await CreateGeneratorWithMathFont();
            var result = await generator.GeneratePdf(html, config);

            var documentElement = RootKids(result.Inspect()).Single();
            var element = Kids(documentElement).Single();

            Assert.Equal("/P", element.StructureType);
            Assert.NotNull(element.Dict.Array("/AF"));
        }

        [Fact]
        public async Task Pdf20_StillProducesFormulaAndAssociatedFile()
        {
            var result = await RenderTagged("<math><mi>x</mi></math>", pdf20: true);

            Assert.Equal("2.0", result.Inspect().Version);
            var documentElement = RootKids(result.Inspect()).Single();
            var formula = Kids(documentElement).Single();
            Assert.Equal("/Formula", formula.StructureType);
            Assert.NotNull(formula.Dict.Array("/AF"));
        }

        static IEnumerable<StructElem> RootKids(PdfFile file) => file.StructureRoot;

        static IEnumerable<StructElem> Kids(StructElem element) => element.Kids;
    }
}
