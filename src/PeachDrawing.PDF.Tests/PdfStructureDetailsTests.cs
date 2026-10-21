using System.Text;
using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>Declaring structure elements ahead of their content, with the language, text and attributes they carry.</summary>
    public class PdfStructureDetailsTests
    {
        private static string Save(Action<PdfComposer, PdfTagRecorder> record)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false, Title = "T" });
            var recorder = composer.CreateTagRecorder();
            composer.AddPage(100, 100);
            record(composer, recorder);
            recorder.Finish();
            using var ms = new MemoryStream();
            composer.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        [Fact]
        public void DeclaredElement_CarriesItsDetailsAndAttributesOfEveryKind()
        {
            var text = Save((composer, recorder) =>
            {
                recorder.BeginPage(composer.Pages[0]);
                recorder.DeclareElement("cell", "TH", details: new PdfStructureDetails
                {
                    Language = "fr",
                    Title = "Header",
                    Attributes =
                    [
                        new PdfStructureAttributes("Table", new Dictionary<string, PdfStructureAttributeValue>
                        {
                            ["Scope"] = PdfStructureAttributeValue.FromName("Column"),
                            ["ColSpan"] = PdfStructureAttributeValue.FromNumber(2),
                            ["Ratio"] = PdfStructureAttributeValue.FromNumber(0.5),
                            ["Summary"] = PdfStructureAttributeValue.FromText("A summary"),
                            ["Sorted"] = PdfStructureAttributeValue.FromBoolean(true),
                            ["Headers"] = PdfStructureAttributeValue.FromList([PdfStructureAttributeValue.FromName("a"), PdfStructureAttributeValue.FromNumber(1)]),
                        }),
                        new PdfStructureAttributes("Layout", new Dictionary<string, PdfStructureAttributeValue> { ["Placement"] = PdfStructureAttributeValue.FromName("Block") }),
                    ],
                });
                Assert.True(recorder.HasElement("cell"));
            });

            Assert.Contains("/S /TH", text);
            Assert.Contains("/Scope /Column", text);
            Assert.Contains("/ColSpan 2", text);
            Assert.Contains("/Ratio 0.5", text);
            Assert.Contains("/Sorted true", text);
            Assert.Contains("/Headers [/a 1]", text);
            Assert.Contains("/O /Layout", text);
            Assert.Matches(@"/A \[", text);                             // two attribute sets are an array
        }

        [Fact]
        public void DeclaredElements_NestUnderTheirParents_InTheOrderDeclared()
        {
            var text = Save((composer, recorder) =>
            {
                recorder.BeginPage(composer.Pages[0]);
                recorder.DeclareElement("list", "L");
                recorder.DeclareElement("item", "LI", "list");
                recorder.DeclareListItemBody("item");
                recorder.DeclareElement("para", "P", "item", parentIsListItemBody: true);
                recorder.DeclareElement("orphan", "Sect", "missing");           // an unknown parent falls back to the current one
                recorder.DeclareListItemBody("missing");                        // and an unknown item has no body to declare
                Assert.False(recorder.HasElement("nothing"));
            });

            Assert.True(text.IndexOf("/S /L", StringComparison.Ordinal) < text.IndexOf("/S /LI", StringComparison.Ordinal));
            Assert.Contains("/S /LBody", text);
            Assert.Contains("/S /P", text);
            Assert.Contains("/S /Sect", text);
        }
    }
}
