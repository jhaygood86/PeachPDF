using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Coverage for <see cref="HtmlContainerInt.DocumentBaseUri"/> — the one place the
    /// "<c>&lt;base href&gt;</c>, else the adapter's base URI" rule lives, read by image/stylesheet
    /// resolution (<c>CommonUtils.ResolveAgainstDocumentBase</c>), link resolution
    /// (<c>HtmlContainer.ResolveHref</c>) and running-element links
    /// (<c>PdfGenerator.HandleRunningElementLinks</c>). The <c>&lt;base&gt;</c> lookup behind it is
    /// memoized per box tree, so both the fresh-tree and the already-resolved read are exercised here,
    /// as is a second document replacing the first one's answer.
    /// </summary>
    public class DocumentBaseUriTests
    {
        private static readonly RUri LoaderBase = new("https://loader.test/docs/page.html");

        private static HtmlContainerInt NewContainer() =>
            new(new PdfSharpAdapter
                {
                    PixelsPerPoint = 1.0,
                    NetworkLoader = new InMemoryNetworkLoader(LoaderBase, string.Empty)
                });

        private static string Document(string head) =>
            $"<!DOCTYPE html><html><head>{head}</head><body>Body</body></html>";

        [Fact]
        public void WithNoDocumentAtAll_FallsBackToTheAdapterBaseUri()
        {
            // Root is still null before SetHtml — the state a <link>/@import stylesheet also sees, since
            // those load from inside GenerateCssTree. (An <img> does not: images resolve during layout.)
            var container = NewContainer();

            Assert.Equal(LoaderBase.AbsoluteUri, container.DocumentBaseUri?.AbsoluteUri);
        }

        [Fact]
        public async Task WithNoBaseElement_FallsBackToTheAdapterBaseUri()
        {
            var container = NewContainer();
            await container.SetHtml(Document(""), null);

            Assert.Equal(LoaderBase.AbsoluteUri, container.DocumentBaseUri?.AbsoluteUri);
        }

        [Fact]
        public async Task WithABaseElement_UsesTheDeclaredBase()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base href='https://example.test/other/'>"), null);

            Assert.Equal("https://example.test/other/", container.DocumentBaseUri?.AbsoluteUri);
        }

        [Fact]
        public async Task WithABlankBaseHref_FallsBackToTheAdapterBaseUri()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base href='   '>"), null);

            Assert.Equal(LoaderBase.AbsoluteUri, container.DocumentBaseUri?.AbsoluteUri);
        }

        [Fact]
        public async Task WithinOneTree_TheBaseIsResolvedOnlyOnce()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base href='https://example.test/other/'>"), null);

            Assert.Equal("https://example.test/other/", container.DocumentBaseUri?.AbsoluteUri);

            // Nothing edits a <base href> after the parse (HtmlTag.SetAttribute's only caller is the
            // dir="auto" pre-pass), so doing it here is not a supported scenario - it is the only way to
            // observe from the outside that the resolution really is memoized per tree rather than
            // re-walked per reference. A read that picked this up would mean the walk is still happening.
            DomUtils.GetBoxByTagName(container.Root!, "base")!.HtmlTag!.SetAttribute("href", "https://changed.test/");

            Assert.Equal("https://example.test/other/", container.DocumentBaseUri?.AbsoluteUri);
        }

        [Fact]
        public async Task ASecondDocumentReplacesTheFirstOnesBase()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base href='https://first.test/a/'>"), null);
            Assert.Equal("https://first.test/a/", container.DocumentBaseUri?.AbsoluteUri);

            // A re-parse produces a new tree, which is what invalidates the memo — a second document
            // must not keep answering with the first one's base.
            await container.SetHtml(Document("<base href='https://second.test/b/'>"), null);
            Assert.Equal("https://second.test/b/", container.DocumentBaseUri?.AbsoluteUri);

            await container.SetHtml(Document(""), null);
            Assert.Equal(LoaderBase.AbsoluteUri, container.DocumentBaseUri?.AbsoluteUri);
        }
    }
}
