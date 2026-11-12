using PeachPDF.Tests.TestSupport;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A closed <c>&lt;details&gt;</c> can be opened in a PDF reader: a hidden widget holds its opened content and a button over the summary shows it
    /// through <c>Hide</c> actions. The page itself is the closed state.
    /// </summary>
    public class DetailsInteractivePdfTests
    {
        private const string Style = "<style>@page { margin: 20pt } body { margin: 0 } summary { height: 20pt } p { margin: 0; height: 40pt; background: rgb(0,160,0) }</style>";

        private static async Task<PeachPdfDocument> RenderAsync(string body, Action<PdfGenerateConfig>? configure = null)
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A5, CompressContentStreams = false };
            configure?.Invoke(config);
            var generator = new PdfGenerator();
            var document = await generator.GeneratePdf($"<html><head>{Style}</head><body>{body}</body></html>", config);
            if (Environment.GetEnvironmentVariable("PEACH_DETAILS_OUT") is { Length: > 0 } path)
            {
                await using var file = File.Create(path);
                document.Save(file);
            }

            return document;
        }

        [Fact]
        public async Task AClosedDetailsGetsAHiddenContentWidgetAndAButtonOverItsSummary()
        {
            var annotations = (await RenderAsync("<details><summary>Closed details</summary><p>content of the details element</p></details><p style='background:rgb(200,0,0);height:20pt'>after the details</p>")).PageAnnotations();

            var widgets = annotations.Where(a => a.Name("/Subtype") == "/Widget").ToList();
            Assert.Equal(2, widgets.Count);

            var content = widgets.Single(w => w.Text("/T")!.EndsWith("Content", StringComparison.Ordinal));
            var open = widgets.Single(w => w.Text("/T")!.EndsWith("Open", StringComparison.Ordinal));

            // Hidden but printable content; a visible button, over the summary at the top of the content.
            Assert.Equal(6, (int)content.Number("/F")!.Value);
            Assert.Equal(4, (int)open.Number("/F")!.Value);
            Assert.NotNull(content.Dict("/AP")?.Raw("/N"));
            Assert.Equal("/Hide", open.Dict("/A")!.Name("/S"));
            Assert.Equal("/Hide", content.Dict("/A")!.Name("/S"));

            var c = content.RectOf();
            var o = open.RectOf();
            Assert.True(c.Height > o.Height, "the opened content reaches below the summary");
            Assert.Equal(o.Left, c.Left, 1);
            Assert.Equal(o.Top, c.Top, 1);
        }

        [Fact]
        public async Task ADocumentWithNoDetailsGetsNoWidgets()
        {
            Assert.Empty((await RenderAsync("<p>plain</p>")).PageAnnotations());
        }

        [Fact]
        public async Task AnOpenDetailsCanBeCollapsed()
        {
            var annotations = (await RenderAsync("<details open><summary>Open</summary><p>content</p></details><p style='background:rgb(200,0,0);height:20pt'>after</p>")).PageAnnotations();

            var widgets = annotations.Where(a => a.Name("/Subtype") == "/Widget").ToDictionary(w => w.Text("/T")!.Split("Disclosure1")[1]);
            Assert.Equal(["Content", "Heading", "Open"], widgets.Keys.Order().ToArray());

            // The page shows the open state: the heading's collapse button is visible, the collapsed heading and the collapsed page are hidden.
            Assert.Equal(4, (int)widgets["Open"].Number("/F")!.Value);
            Assert.Equal(6, (int)widgets["Heading"].Number("/F")!.Value);
            Assert.Equal(6, (int)widgets["Content"].Number("/F")!.Value);
            Assert.Equal("/Hide", widgets["Open"].Dict("/A")!.Name("/S"));
            Assert.Equal("/Hide", widgets["Heading"].Dict("/A")!.Name("/S"));
            Assert.Null(widgets["Content"].Dict("/A"));

            // The collapsed page starts below the heading and reaches down the page.
            Assert.True(widgets["Content"].RectOf().Top <= widgets["Heading"].RectOf().Bottom + 0.5);
            Assert.True(widgets["Content"].RectOf().Height > widgets["Heading"].RectOf().Height);
        }

        [Fact]
        public async Task TheWidgetsOfOpenDetailsComeBeforeThoseOfClosedOnes()
        {
            // A closed details' opened content can reach over an open one below it. It is drawn over the open one's collapsed page only if its widget
            // is later in the page, and a reader that hit-tests in order (PDFium) lets a hidden widget that covers a heading take its clicks otherwise.
            var annotations = (await RenderAsync("<details><summary>Closed</summary><p>c</p></details><details open><summary>Open</summary><p>o</p></details><p>after</p>")).PageAnnotations();

            var names = annotations.Where(a => a.Name("/Subtype") == "/Widget").Select(a => a.Text("/T")!).ToList();
            Assert.Equal(5, names.Count);
            var lastOpenOne = names.FindLastIndex(n => n.Contains("Disclosure1", StringComparison.Ordinal));
            var firstClosedOne = names.FindIndex(n => n.Contains("Disclosure2", StringComparison.Ordinal));
            Assert.True(lastOpenOne < firstClosedOne, string.Join(", ", names));
        }

        [Fact]
        public async Task AnOpenDetailsThatHoldsAnotherIsNotOfferedForCollapsing()
        {
            var annotations = (await RenderAsync("<details open><summary>Outer</summary><details open><summary>Inner</summary><p>x</p></details></details>")).PageAnnotations();

            Assert.DoesNotContain(annotations, a => a.Name("/Subtype") == "/Widget");
        }

        [Fact]
        public async Task TheOptionAndTheArchivalProfilesLeaveOnlyTheClosedPage()
        {
            const string body = "<details><summary>Closed</summary><p>content</p></details>";

            Assert.Empty((await RenderAsync(body, c => c.EnableInteractivePdfDetails = false)).PageAnnotations());
            Assert.Empty((await RenderAsync(body, c => { c.PdfAConformance = PdfAConformance.PdfA2B; c.Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow }; })).PageAnnotations());
        }

        [Fact]
        public async Task ADetailsInsideAClosedOneIsNotOfferedAndSeveralAreEachOffered()
        {
            var one = await RenderAsync("<details><summary>Outer</summary><details><summary>Inner</summary><p>inner</p></details></details>");
            Assert.Equal(2, one.PageAnnotations().Count);

            var two = await RenderAsync("<details><summary>One</summary><p>a</p></details><details><summary>Two</summary><p>b</p></details>");
            Assert.Equal(4, two.PageAnnotations().Count);
        }
    }
}
