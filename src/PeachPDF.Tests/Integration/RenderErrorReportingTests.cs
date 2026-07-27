using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.Network;
using PeachPDF.Tests.TestSupport;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// What happens when a phase of the pipeline throws: the exception is wrapped in an
    /// <see cref="HtmlRenderException"/> naming the phase, and the render fails.
    /// </summary>
    /// <remarks>
    /// These paths had no coverage at all, which is how <c>ReportError</c> — a name that reads as
    /// advisory, on a method that always threw — kept two callers written as if it returned. It is now
    /// <c>container.RenderError(…)</c>, which builds the exception and leaves the <c>throw</c> to the
    /// call site, so a statement written after one is dead code the compiler reports. These tests pin
    /// the behaviour that rewrite must not have changed: same phase, same message, original exception
    /// preserved as the innermost cause.
    /// </remarks>
    public class RenderErrorReportingTests
    {
        /// <summary>A box whose layout fails, for exercising the layout engines' own error handling.</summary>
        private sealed class ThrowingBox : CssBox
        {
            internal const string Marker = "layout blew up";

            internal ThrowingBox(CssBox parent) : base(parent, null)
            {
                InheritStyle(parent, everything: true);
                Display = CssConstants.Block;
            }

            protected override ValueTask PerformLayoutImp(RGraphics g) =>
                throw new InvalidOperationException(Marker);
        }

        private sealed class ThrowingGraphics : TestRecordingGraphics
        {
            internal const string Marker = "paint blew up";

            public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size,
                bool rtl, double letterSpacing = 0, RFontPalette? fontPalette = null) =>
                throw new InvalidOperationException(Marker);
        }

        /// <summary>A loader whose every resource fetch fails, the way a broken or offline one would.</summary>
        private sealed class ThrowingNetworkLoader(string primaryHtml) : RNetworkLoader
        {
            internal const string Marker = "fetch blew up";

            public override RUri? BaseUri => new("https://example.test/index.html");

            public override Task<string> GetPrimaryContents() => Task.FromResult(primaryHtml);

            public override Task<RNetworkResponse?> GetResourceStream(RUri uri) =>
                throw new InvalidOperationException(Marker);
        }

        private static Exception Innermost(Exception exception)
        {
            while (exception.InnerException is { } inner) exception = inner;
            return exception;
        }

        /// <summary>
        /// Every message in the exception's cause chain. An engine's own failure is not necessarily the
        /// outermost one: the container box it belongs to catches and re-raises on the way out, so the
        /// engine names itself somewhere inside the chain rather than at its head.
        /// </summary>
        private static IEnumerable<string> Messages(Exception exception)
        {
            for (Exception? e = exception; e is not null; e = e.InnerException) yield return e.Message;
        }

        /// <summary>
        /// Each layout engine catches whatever its own layout throws and re-raises it as a
        /// <see cref="HtmlRenderErrorType.Layout"/> failure naming the engine.
        /// </summary>
        [Theory]
        [InlineData("display:grid", "Failed grid layout")]
        [InlineData("display:flex", "Failed flex layout")]
        [InlineData("display:table", "Failed table layout")]
        [InlineData("columns:2", "Failed multi-column layout")]
        public async Task AnEngineWhoseLayoutThrows_FailsTheRenderNamingThatEngine(string style, string message)
        {
            var thrown = await Assert.ThrowsAsync<HtmlRenderException>(async () =>
                await LayoutHarness.LayoutAsync(
                    LayoutHarness.Wrap($"<div style='{style}'><div id='item'>item</div></div>"),
                    prepare: root => _ = new ThrowingBox(LayoutHarness.FindById(root, "item")!)));

            Assert.Contains(message, Messages(thrown));
            Assert.Equal(HtmlRenderErrorType.Layout, thrown.RenderErrorType);
            Assert.Equal(ThrowingBox.Marker, Innermost(thrown).Message);
        }

        /// <summary>
        /// The same for an ordinary block-flow box, which reports through <c>CssBox.PerformLayout</c>
        /// rather than through an engine.
        /// </summary>
        [Fact]
        public async Task ABlockBoxWhoseLayoutThrows_FailsTheRenderAsALayoutError()
        {
            var thrown = await Assert.ThrowsAsync<HtmlRenderException>(async () =>
                await LayoutHarness.LayoutAsync(
                    LayoutHarness.Wrap("<div id='host'></div>"),
                    prepare: root => _ = new ThrowingBox(LayoutHarness.FindById(root, "host")!)));

            Assert.Equal(HtmlRenderErrorType.Layout, thrown.RenderErrorType);
            Assert.Equal(ThrowingBox.Marker, Innermost(thrown).Message);
        }

        /// <summary>
        /// A paint that throws fails the render as a <see cref="HtmlRenderErrorType.Paint"/> error.
        /// </summary>
        [Fact]
        public async Task APaintThatThrows_FailsTheRenderAsAPaintError()
        {
            var (_, container) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap("<p>alpha</p>"));

            var thrown = Assert.Throws<HtmlRenderException>(
                () => FragmentPaintHarness.PaintPage(container, new ThrowingGraphics()));

            Assert.Equal("Exception in box paint", thrown.Message);
            Assert.Equal(HtmlRenderErrorType.Paint, thrown.RenderErrorType);
            Assert.Equal(ThrowingGraphics.Marker, Innermost(thrown).Message);
        }

        /// <summary>
        /// A stylesheet reference that is merely <i>absent</i> is degraded deliberately — the rest of the
        /// document renders — but a loader that <i>throws</i> fails the whole render. That asymmetry is
        /// the reason this handler had a dead <c>return (null, null)</c> after its error call, and both
        /// its callers still null-check a result that can never be null.
        /// </summary>
        [Fact]
        public async Task AStylesheetFetchThatThrows_FailsTheRenderAsACssError()
        {
            var loader = new ThrowingNetworkLoader(
                """<!DOCTYPE html><html><head><link rel="stylesheet" href="site.css"></head><body>x</body></html>""");

            var thrown = await Assert.ThrowsAsync<HtmlRenderException>(
                async () => await new PdfGenerator().GeneratePdf(null, new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 }));

            Assert.Equal("Exception in handling stylesheet source", thrown.Message);
            Assert.Equal(HtmlRenderErrorType.CssParsing, thrown.RenderErrorType);
            Assert.Equal(ThrowingNetworkLoader.Marker, Innermost(thrown).Message);
        }

        /// <summary>
        /// A stylesheet the loader simply does not have is <i>not</i> an error: the document still
        /// renders. The control for the case above.
        /// </summary>
        [Fact]
        public async Task AMissingStylesheet_StillRendersTheDocument()
        {
            var loader = new InMemoryNetworkLoader(new RUri("https://example.test/index.html"),
                """<!DOCTYPE html><html><head><link rel="stylesheet" href="site.css"></head><body>x</body></html>""");

            var document = await new PdfGenerator().GeneratePdf(null, new PdfGenerateConfig { NetworkLoader = loader, PageSize = PageSize.A4 });

            Assert.True(document.PageCount >= 1);
        }
    }
}
