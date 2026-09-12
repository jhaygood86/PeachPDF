using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.Structure;
using System.Collections.Generic;
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
        static string FontFace() => BundledFonts.FontFaceRule(BundledFonts.Math, "TestMath", "font/truetype");

        static async Task<PeachPdfDocument> RenderTagged(string mathHtml, bool pdf20 = false)
        {
            var html = $"<html><head><style>{FontFace()} math {{ font-family: TestMath; }}</style></head><body>{mathHtml}</body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                EnableTaggedPdf = true,
                PdfVersion = pdf20 ? PdfVersion.Pdf20 : PdfVersion.Pdf17,
            };
            return await new PdfGenerator().GeneratePdf(html, config);
        }

        [Fact]
        public async Task Math_DefaultsToFormulaStructureType()
        {
            var result = await RenderTagged("<math><mi>x</mi></math>");

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var formula = Kids(documentElement).Single();

            Assert.Equal("/Formula", formula.StructureType);
        }

        [Fact]
        public async Task Formula_HasAssociatedFile_WithMathMlSourceAndSupplementRelationship()
        {
            var result = await RenderTagged("<math><mi>x</mi><mo>+</mo><mn>1</mn></math>");

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var formula = Kids(documentElement).Single();

            var afArray = formula.Elements.GetArray("/AF");
            Assert.NotNull(afArray);
            var fileSpec = (PdfFileSpecification)((PdfReference)afArray!.Elements[0]).Value;

            Assert.Equal("/Supplement", fileSpec.AssociatedFileRelationship);
            Assert.Equal("formula.mml", fileSpec.FileName);
            // The in-memory model stores this raw (with its literal "/") - PdfWriter.Write(PdfName)
            // only escapes it to "#2F" when actually serializing to PDF syntax, not before - see
            // PdfAssociatedFileTests' identical note.
            Assert.Equal("/application/mathml+xml", fileSpec.EmbeddedFile.MimeType);

            var mathml = Encoding.UTF8.GetString(fileSpec.EmbeddedFile.Stream.Value);
            Assert.Contains("<mi>x</mi>", mathml);
            Assert.Contains("<mo>+</mo>", mathml);
            Assert.Contains("<mn>1</mn>", mathml);
            Assert.StartsWith("<math", mathml);
        }

        [Fact]
        public async Task Formula_AssociatedFile_AlsoIndexedInCatalogLevelAf()
        {
            var result = await RenderTagged("<math><mi>x</mi></math>");

            var catalogAf = result.PdfDocument.Catalog.Elements.GetArray("/AF");
            Assert.NotNull(catalogAf);
            Assert.Single(catalogAf!.Elements);
        }

        [Fact]
        public async Task NoTagging_NoAssociatedFileWritten()
        {
            var html = $"<html><head><style>{FontFace()} math {{ font-family: TestMath; }}</style></head>" +
                       "<body><math><mi>x</mi></math></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4 };
            var result = await new PdfGenerator().GeneratePdf(html, config);

            Assert.False(result.PdfDocument.Catalog.Elements.ContainsKey("/AF"));
        }

        [Fact]
        public async Task AuthorOverride_RetargetsFormulaToAnotherType_StillAttachesSource()
        {
            var html = $"<html><head><style>{FontFace()} math {{ font-family: TestMath; -peachpdf-pdf-tag-type: P; }}</style></head>" +
                       "<body><math><mi>x</mi></math></body></html>";
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true };
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var element = Kids(documentElement).Single();

            Assert.Equal("/P", element.StructureType);
            Assert.NotNull(element.Elements.GetArray("/AF"));
        }

        [Fact]
        public async Task Pdf20_StillProducesFormulaAndAssociatedFile()
        {
            var result = await RenderTagged("<math><mi>x</mi></math>", pdf20: true);

            Assert.Equal(20, result.PdfDocument.Version);
            var documentElement = RootKids(result.PdfDocument.Catalog.StructureTreeRoot).Single();
            var formula = Kids(documentElement).Single();
            Assert.Equal("/Formula", formula.StructureType);
            Assert.NotNull(formula.Elements.GetArray("/AF"));
        }

        static IEnumerable<PdfStructureElement> RootKids(PdfStructureTreeRoot root) =>
            PdfStructureElement.GetKids(root.Elements).Cast<PdfStructureElement>();

        static IEnumerable<PdfStructureElement> Kids(PdfStructureElement element) =>
            PdfStructureElement.GetKids(element.Elements).Cast<PdfStructureElement>();
    }
}
