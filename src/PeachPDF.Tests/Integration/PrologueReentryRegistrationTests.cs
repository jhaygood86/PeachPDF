using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.Utilities;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A box's prologue can run more than once inside a single layout invocation: a break decision
    /// taken against a <i>finished</i> box re-lays it out at its new position, and that re-entry runs
    /// the prologue again so the box's rectangles reset and its position re-derives.
    /// </summary>
    /// <remarks>
    /// Both of the prologue's registrations append to a document-level list, so a re-entry that does
    /// not withdraw the previous run's entries leaves the box registered twice, each entry recording a
    /// position it no longer occupies. For named pages that is not merely untidy:
    /// <see cref="HtmlContainerInt.ActivePageName"/> reads the <i>last</i> entry, and it is what
    /// decides whether the following box forces a page break at all.
    /// <para>
    /// The fixture drives the re-entry through the keep-with-next first-line retry, which is the one
    /// mover that already re-ran a box in place — so these assertions hold on the mechanism as it
    /// stood before the rest of the break-decision work, not only after it.
    /// </para>
    /// </remarks>
    public class PrologueReentryRegistrationTests
    {
        // A heading chained to the paragraph by the UA default `h2 { page-break-after: avoid }`, with
        // the paragraph pinned so word flow pushes its first line across the boundary — which is what
        // makes the retry fire and the paragraph's own prologue run a second time.
        //
        // Deliberately no `page` name on the paragraph: an explicit one forces a break of its own, so
        // the box would be placed at the page top and its first line would never be pushed. The
        // named-page half of the withdrawal is exercised where a mover can relocate a named box —
        // see EarlyBreakLayoutIntegrationTests.
        private const string Section = """
            <h2 class='heading'>Section heading</h2>
            <p class='para' style='margin:0;string-set:section content(text)'>A plain paragraph of body text that follows the heading and whose first line lands across the page boundary.</p>
            """;

        [Fact]
        public async Task RetriedBox_RegistersItsNamedStringExactlyOnce()
        {
            var (root, container) = await BuildAsync(Document(Section));

            AssertTheRetryActuallyFired(root, container);

            Assert.Equal(1, container.NamedStrings.Count(s => s.Name == "section"));
        }

        // The registered entry must describe where the box ended up, not where it was first tried.
        [Fact]
        public async Task RetriedBox_RegistrationRecordsTheFinalPosition()
        {
            var (root, container) = await BuildAsync(Document(Section));

            var para = AssertTheRetryActuallyFired(root, container);

            var namedString = Assert.Single(container.NamedStrings, s => s.Name == "section");

            Assert.Equal(para.Location.Y, namedString.Y, 6);
        }

        // A document that never re-enters a prologue must be completely unaffected by the withdrawal.
        [Fact]
        public async Task BoxLaidOutOnce_StillRegistersExactlyOnce()
        {
            var (root, container) = await BuildAsync(Document(Section, fillerHeight: 100));

            var para = FindByClass(root, "para")!;

            Assert.Equal(0, Math.Floor(para.Location.Y / container.PageSize.Height));
            Assert.Equal(1, container.NamedStrings.Count(s => s.Name == "section"));
        }

        /// <summary>
        /// Guards the fixture rather than the fix: if the paragraph stops being relocated, the counts
        /// above would pass whatever the withdrawal did.
        /// </summary>
        private static CssBox AssertTheRetryActuallyFired(CssBox root, HtmlContainerInt container)
        {
            var heading = FindByClass(root, "heading")!;
            var para = FindByClass(root, "para")!;
            var pageHeight = container.PageSize.Height;

            var paraPage = Math.Floor(para.Location.Y / pageHeight);

            Assert.True(paraPage >= 1, $"fixture must relocate the paragraph off page 1, but it is at y={para.Location.Y}");
            Assert.Equal(paraPage, Math.Floor(heading.Location.Y / pageHeight));

            return para;
        }

        #region Helpers

        // Mirrors KeepWithNextIntegrationTests' filler convention: a fixed-height filler pins the
        // section near the bottom of page 1 deterministically, and the body margin is pinned in pt so
        // the boundary-sensitive geometry stays exact.
        private static string Document(string section, double fillerHeight = 700) =>
            $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <style>
            @page { size: A4; margin: 20mm; }
            body { margin: 8pt; }
            h2 { margin: 6pt 0; }
            </style>
            </head>
            <body>
            <div class='filler' style='height: {{fillerHeight}}pt'>filler</div>
            {{section}}
            </body>
            </html>
            """;

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAsync(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);

            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = Utils.Convert(size, 1.0);
            container.MaxSize = Utils.Convert(size, 1.0);
            container.MarginTop = 20;
            container.MarginBottom = 20;

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindByClass(CssBox root, string className)
        {
            var classAttr = root.HtmlTag?.TryGetAttribute("class", "");

            if (!string.IsNullOrEmpty(classAttr)
                && classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(className))
            {
                return root;
            }

            return root.Boxes.Select(child => FindByClass(child, className)).FirstOrDefault(found => found is not null);
        }

        #endregion
    }
}
