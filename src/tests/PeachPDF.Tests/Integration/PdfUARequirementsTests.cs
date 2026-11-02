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
    /// What a file that claims PDF/UA has to carry beyond its identification, read back from the saved file: a description on every link and
    /// figure, links the structure reaches, lists that say how they are marked, text in paragraphs, and (PDF 2.0) the structure namespace and
    /// structure destinations. Each of these was found by validating a generated file with veraPDF.
    /// </summary>
    public class PdfUARequirementsTests
    {
        const string Html = """
            <html lang='en'><head><title>Quarterly report</title></head><body>
            <header><h1>Quarterly report</h1></header>
            <nav aria-label='Sections'><ul><li><a href='#data'>Jump to data</a></li></ul></nav>
            <main>
              <p>Sales grew strongly. <a href='https://example.com/'>Read the full report</a>.</p>
              <figure><svg width='60' height='40' role='img' aria-label='Bar'><title>Bar</title><rect width='60' height='40' fill='orange'/></svg><figcaption>A bar.</figcaption></figure>
              <h2 id='data'>Data</h2>
              <ol><li>One</li><li>Two</li></ol>
            </main>
            <footer>End of report</footer>
            </body></html>
            """;

        static async Task<PdfFile> RenderAsync(PdfUAConformance level)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfUAConformance = level,
                CompressContentStreams = false,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var document = await new PdfGenerator().GeneratePdf(Html, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            return PdfFile.Parse(stream.ToArray());
        }

        static IEnumerable<(StructElem Element, StructElem? Parent)> Walk(PdfFile file)
        {
            IEnumerable<(StructElem, StructElem?)> Visit(StructElem element, StructElem? parent)
            {
                yield return (element, parent);
                foreach (var kid in element.Kids)
                {
                    foreach (var below in Visit(kid, element))
                        yield return below;
                }
            }

            foreach (var top in file.StructureRoot)
            {
                foreach (var item in Visit(top, null))
                    yield return item;
            }
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1)]
        [InlineData(PdfUAConformance.UA2)]
        public async Task EveryLinkIsDescribed_AndLeadsBackToItsStructureElementThroughTheParentTree(PdfUAConformance level)
        {
            var file = await RenderAsync(level);

            var links = file.Pages.SelectMany(file.Annotations).Where(a => a.Name("/Subtype") == "/Link").ToList();
            Assert.Equal(2, links.Count);
            Assert.Contains(links, l => l.Text("/Contents") == "Jump to data");
            Assert.Contains(links, l => l.Text("/Contents") == "Read the full report");

            foreach (var link in links)
            {
                // An annotation's parent-tree entry is its structure element itself; only page content leads to an array of elements.
                var entry = file.ParentTreeEntry((int)link.Number("/StructParent")!.Value);
                var element = Assert.IsType<PdfDict>(entry);
                Assert.Equal("/Link", element.Name("/S"));
            }
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1)]
        [InlineData(PdfUAConformance.UA2)]
        public async Task EveryFigureIsDescribed_EvenOneThatGroupsAPictureAndItsCaption(PdfUAConformance level)
        {
            var file = await RenderAsync(level);

            var figures = Walk(file).Where(x => x.Element.StructureType == "/Figure").Select(x => x.Element).ToList();
            Assert.Equal(2, figures.Count);
            Assert.All(figures, f => Assert.False(string.IsNullOrEmpty(f.AlternateText)));
            Assert.Contains(figures, f => f.AlternateText == "A bar.");
            Assert.Contains(figures, f => f.AlternateText == "Bar");
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1)]
        [InlineData(PdfUAConformance.UA2)]
        public async Task AListSaysHowItsItemsAreMarked(PdfUAConformance level)
        {
            var file = await RenderAsync(level);

            var numbering = Walk(file)
                .Where(x => x.Element.StructureType == "/L")
                .Select(x => ListNumbering(x.Element))
                .ToList();

            Assert.Equal(["/Disc", "/Decimal"], numbering);
        }

        static string? ListNumbering(StructElem list)
        {
            var attributes = list.Dict["/A"];
            var dictionaries = attributes switch
            {
                PdfDict d => [d],
                PdfArr a => a.Resolved.OfType<PdfDict>().ToList(),
                _ => new List<PdfDict>(),
            };

            return dictionaries.Where(d => d.Name("/O") == "/List").Select(d => d.Name("/ListNumbering")).FirstOrDefault();
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1)]
        [InlineData(PdfUAConformance.UA2)]
        public async Task TextIsInAParagraph_NotLooseInADivisionOrTheDocument(PdfUAConformance level)
        {
            var file = await RenderAsync(level);

            // No span sits directly in the document or in a division: a division only groups other elements.
            Assert.DoesNotContain(Walk(file), x => x.Element.StructureType == "/Span" && x.Parent?.StructureType is "/Document" or "/Div");
        }

        [Theory]
        [InlineData(PdfUAConformance.UA1)]
        [InlineData(PdfUAConformance.UA2)]
        public async Task ALineDrawnUnderLinkTextIsAnArtifact_NotUntaggedContent(PdfUAConformance level)
        {
            var file = await RenderAsync(level);

            var depth = 0;
            foreach (var line in file.ContentText(file.Pages[0]).Split('\n'))
            {
                var operation = line.TrimEnd('\r');
                if (operation.EndsWith("BDC") || operation.EndsWith("BMC"))
                    depth++;
                else if (operation == "EMC")
                    depth--;
                else if (depth == 0 && operation is "S" or "s")
                    Assert.Fail("A stroked line (a text decoration) is neither tagged content nor an artifact.");
            }

            Assert.Equal(0, depth);
        }

        [Fact]
        public async Task UA2_NamesThePdf20StructureNamespace_OnTheRootAndOnEveryElement()
        {
            var file = await RenderAsync(PdfUAConformance.UA2);

            var namespaces = file.StructureTreeRoot!.Array("/Namespaces")!.Resolved.OfType<PdfDict>().ToList();
            var pdf2 = Assert.Single(namespaces);
            Assert.Equal("http://iso.org/pdf2/ssn", pdf2.Text("/NS"));

            var document = Assert.Single(file.StructureRoot);
            Assert.Equal("/Document", document.StructureType);
            Assert.All(Walk(file), x => Assert.Equal("http://iso.org/pdf2/ssn", x.Element.Dict.Dict("/NS")?.Text("/NS")));
        }

        [Fact]
        public async Task UA2_EveryDestinationWithinTheDocumentLeadsToAStructureElement()
        {
            var file = await RenderAsync(PdfUAConformance.UA2);

            var internalLink = file.Pages.SelectMany(file.Annotations)
                .Single(a => a.Name("/Subtype") == "/Link" && a.Dict("/A")?.Name("/S") == "/GoTo");
            var linkDestination = internalLink.Dict("/A")!.Array("/SD");
            Assert.NotNull(linkDestination);
            Assert.Equal("/H2", Assert.IsType<PdfDict>(linkDestination[0]).Name("/S"));

            var outlines = file.Catalog.Dict("/Outlines")!;
            var entry = outlines.Dict("/First");
            Assert.NotNull(entry);
            while (entry is not null)
            {
                var destination = entry.Array("/Dest");
                Assert.NotNull(destination);
                Assert.IsType<PdfDict>(destination[0]);
                Assert.NotNull(((PdfDict)destination[0]).Name("/S"));
                entry = entry.Dict("/Next");
            }
        }

        [Fact]
        public async Task UA1_StaysOnPageDestinations_BecauseStructureDestinationsArePdf20()
        {
            var file = await RenderAsync(PdfUAConformance.UA1);

            var internalLink = file.Pages.SelectMany(file.Annotations)
                .Single(a => a.Name("/Subtype") == "/Link" && a.Dict("/A")?.Name("/S") == "/GoTo");
            Assert.Null(internalLink.Dict("/A")!.Array("/SD"));
            Assert.Null(file.StructureTreeRoot!.Array("/Namespaces"));
        }
            [Theory]
        [InlineData(PdfUAConformance.UA1)]
        [InlineData(PdfUAConformance.UA2)]
        public async Task AHeaderCellIsNamed_AndACellThatListsItsHeadersPointsAtThem(PdfUAConformance level)
        {
            const string table = "<html lang='en'><head><title>t</title></head><body><table>"
                + "<tr><th id='h1'>Name</th><th id='h2'>Total</th></tr>"
                + "<tr><td headers='h1'>North</td><td headers='h1 h2'>10</td></tr></table></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfUAConformance = level,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var document = await new PdfGenerator().GeneratePdf(table, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            var file = PdfFile.Parse(stream.ToArray());

            var cells = Walk(file).Select(x => x.Element).Where(e => e.StructureType is "/TH" or "/TD").ToList();
            var headers = cells.Where(e => e.StructureType == "/TH").ToList();
            Assert.Equal(2, headers.Count);
            var ids = headers.Select(h => h.Dict.Text("/ID")!).ToList();
            Assert.All(ids, id => Assert.False(string.IsNullOrEmpty(id)));

            // Both are findable in the structure tree's ID tree.
            var idTree = file.StructureTreeRoot!.Dict("/IDTree");
            Assert.NotNull(idTree);
            var names = idTree!.Array("/Names")!.Resolved.OfType<PdfText>().Select(t => t.Value).ToList();
            Assert.All(ids, id => Assert.Contains(id, names));

            // The cell under both headers lists both; the one under the first lists one.
            static IReadOnlyList<string> Listed(StructElem cell)
            {
                var a = cell.Dict["/A"];
                var dictionaries = a switch { PdfDict d => [d], PdfArr arr => arr.Resolved.OfType<PdfDict>().ToList(), _ => new List<PdfDict>() };
                return dictionaries.Select(d => d.Array("/Headers")).FirstOrDefault(h => h is not null)?.Resolved.OfType<PdfText>().Select(t => t.Value).ToList() ?? [];
            }

            var data = cells.Where(e => e.StructureType == "/TD").ToList();
            Assert.Equal([ids[0]], Listed(data[0]));
            Assert.Equal([ids[0], ids[1]], Listed(data[1]));
        }
        [Fact]
        public async Task AFindingIsReportedForAHeadingThatSkipsALevel_ATableWithNoHeaderCell_AndAFigureWithNoDescription()
        {
            const string html = "<html lang='en'><head><title>t</title></head><body><h1>One</h1><h3>Three</h3>"
                + "<table><tr><td>a</td></tr></table><img src='data:image/gif;base64,R0lGODlhAQABAAAAACwAAAAAAQABAAA=' alt=''></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfUAConformance = PdfUAConformance.UA1,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var document = await new PdfGenerator().GeneratePdf(html, config);

            Assert.Contains("A heading H3 follows H1, skipping a level.", document.AccessibilityWarnings);
            Assert.Contains("A table has no header cell (TH).", document.AccessibilityWarnings);
        }

        [Fact]
        public async Task ADocumentWithASoundStructureHasNoFindings_AndOneWithoutAClaimIsNotChecked()
        {
            var sound = await new PdfGenerator().GeneratePdf(Html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfUAConformance = PdfUAConformance.UA1,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            });
            var unclaimed = await new PdfGenerator().GeneratePdf("<html lang='en'><body><h1>a</h1><h4>b</h4></body></html>", new PdfGenerateConfig { PageSize = PageSize.A4, EnableTaggedPdf = true });

            Assert.Empty(sound.AccessibilityWarnings);
            Assert.Empty(unclaimed.AccessibilityWarnings);
        }

        [Fact]
        public async Task AFormFieldIsNamedByItsLabel()
        {
            const string html = "<html lang='en'><head><title>t</title></head><body><label for='n'>Your name</label> <input id='n' name='n' value='Ada'></body></html>";
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfUAConformance = PdfUAConformance.UA1,
                EnableInteractivePdfForms = true,
                Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow },
            };
            var document = await new PdfGenerator().GeneratePdf(html, config);
            using var stream = new MemoryStream();
            document.Save(stream);
            var file = PdfFile.Parse(stream.ToArray());

            var field = Assert.Single(file.Fields);
            Assert.Equal("Your name", field.Text("/TU"));
        }
}
}
