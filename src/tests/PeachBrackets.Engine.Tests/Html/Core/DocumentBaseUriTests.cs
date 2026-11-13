using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Utils;
using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Html.Core
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
        private static readonly Uri LoaderBase = new("https://loader.test/docs/page.html");

        private static HtmlContainerInt NewContainer() =>
            new(new HtmlRenderContext
                {
                    PixelsPerPoint = 1.0,
                    NetworkLoader = new InMemoryNetworkLoader(LoaderBase, string.Empty)
                });

        private static string Document(string head) =>
            $"<!DOCTYPE html><html><head>{head}</head><body>Body</body></html>";

        [Fact]
        public void WithNoDocumentAtAll_FallsBackToTheAdapterBaseUri()
        {
            // Nothing has been parsed yet. (A <link>/@import stylesheet does not see this state any more: while the stylesheets are
            // collected the tree being built is already the document base's source - see the tests below.)
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

        [Fact]
        public async Task ARelativeBaseHref_ResolvesAgainstTheAdapterBaseUri()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base href='../other/'>"), null);

            Assert.Equal("https://loader.test/other/", container.DocumentBaseUri?.AbsoluteUri);
        }

        [Fact]
        public async Task ABaseElementWithoutAnHref_DoesNotHideALaterOneThatHasIt()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base target='_blank'><base href='https://example.test/later/'>"), null);

            Assert.Equal("https://example.test/later/", container.DocumentBaseUri?.AbsoluteUri);
        }

        private static HtmlContainerInt NewContainerWithoutBase(InMemoryNetworkLoader loader) =>
            new(new HtmlRenderContext { PixelsPerPoint = 1.0, NetworkLoader = loader });

        private static string? ParagraphColor(HtmlContainerInt container) =>
            DomUtils.GetBoxByTagName(container.Root!, "p")!.Color;

        [Fact]
        public async Task ALinkedStylesheet_ResolvesAgainstTheBaseElement_NotTheAdapterBaseUri()
        {
            var loader = new InMemoryNetworkLoader(LoaderBase, string.Empty);
            loader.AddTextResource("https://example.test/other/s.css", "p { color: red }", "text/css");
            // The same file name next to the page would be the wrong answer: it must not be fetched.
            loader.AddTextResource("https://loader.test/docs/s.css", "p { color: blue }", "text/css");
            var container = NewContainerWithoutBase(loader);

            await container.SetHtml("<!DOCTYPE html><html><head><base href='https://example.test/other/'><link rel=stylesheet href='s.css'></head><body><p>x</p></body></html>", null);

            Assert.Equal("rgb(255, 0, 0)", ParagraphColor(container));
        }

        [Fact]
        public async Task AnImportInAStyleElement_ResolvesAgainstTheBaseElement()
        {
            var loader = new InMemoryNetworkLoader(LoaderBase, string.Empty);
            loader.AddTextResource("https://example.test/other/imported.css", "p { color: red }", "text/css");
            var container = NewContainerWithoutBase(loader);

            await container.SetHtml("<!DOCTYPE html><html><head><base href='https://example.test/other/'><style>@import 'imported.css';</style></head><body><p>x</p></body></html>", null);

            Assert.Equal("rgb(255, 0, 0)", ParagraphColor(container));
        }

        [Fact]
        public async Task ALinkedStylesheet_WithABaseThatIsOnlyRelative_ResolvesAgainstTheAdapterBaseUri()
        {
            var loader = new InMemoryNetworkLoader(LoaderBase, string.Empty);
            loader.AddTextResource("https://loader.test/other/s.css", "p { color: red }", "text/css");
            var container = NewContainerWithoutBase(loader);

            await container.SetHtml("<!DOCTYPE html><html><head><base href='../other/'><link rel=stylesheet href='s.css'></head><body><p>x</p></body></html>", null);

            Assert.Equal("rgb(255, 0, 0)", ParagraphColor(container));
        }

        [Fact]
        public async Task TheTreeHandedToTheParse_IsNotKeptAfterwards()
        {
            var container = NewContainer();
            await container.SetHtml(Document("<base href='https://example.test/other/'>"), null);

            Assert.Null(container.ParsingRoot);
        }
    }
}
