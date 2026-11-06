using PeachBrackets;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Network;
using PeachPDF.Rendering;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// <see cref="HtmlDocumentOptions.SynchronousOnly"/>: a host that says layout needs nothing asynchronous loads, lays out and paints
    /// without waiting, and gets an <see cref="InvalidOperationException"/> naming the resource if the document then tries to suspend.
    /// </summary>
    public class SynchronousOnlyTests
    {
        private const string Origin = "https://example.test/";

        /// <summary>A loader whose answer is either already known (a completed task) or never comes (a pending one).</summary>
        private sealed class ScriptedLoader(bool completed) : RNetworkLoader
        {
            public List<string> Requested { get; } = [];

            public override Uri? BaseUri => new(Origin + "index.html");

            public override Task<string> GetPrimaryContents() => Task.FromResult(string.Empty);

            public override Task<RNetworkResponse?> GetResourceStream(Uri uri)
            {
                Requested.Add(uri.AbsoluteUri);
                if (!completed)
                    return new TaskCompletionSource<RNetworkResponse?>().Task;

                if (uri.AbsoluteUri.EndsWith(".css", StringComparison.Ordinal))
                {
                    var css = new MemoryStream(Encoding.UTF8.GetBytes("p { color: rgb(255, 0, 0) }"));
                    return Task.FromResult<RNetworkResponse?>(new RNetworkResponse(css, new Dictionary<string, string[]> { ["Content-Type"] = ["text/css"] }));
                }

                return Task.FromResult<RNetworkResponse?>(null);
            }
        }

        private static HtmlDocumentOptions Synchronous() => new() { SynchronousOnly = true, MediaType = MediaType.Screen };

        private static (HtmlRenderContext Context, ScriptedLoader Loader) NewContext(bool completed)
        {
            var loader = new ScriptedLoader(completed);
            return (new HtmlRenderContext { NetworkLoader = loader }, loader);
        }

        private static HtmlDocument LoadAndLayout(HtmlRenderContext context, string html)
        {
            var document = HtmlDocument.Load(html, context, Synchronous());
            var area = document.ContentArea;
            using var measuring = PdfCanvas.CreateMeasuring(context, area.Width, area.Height);
            document.Layout(measuring);
            return document;
        }

        [Fact]
        public void InlineHtml_LoadsLaysOutAndPaintsWithoutWaiting()
        {
            var (context, _) = NewContext(completed: true);
            using var document = LoadAndLayout(context, "<p style='margin:0'>Hello</p><div style='height:40pt;background:red'></div>");

            var page = Assert.Single(document.Pages);
            using var canvas = PdfCanvas.CreateMeasuring(context, page.WidthPoints, page.HeightPoints);
            document.PaintPage(0, canvas);
        }

        [Fact]
        public async Task TheSynchronousResult_IsTheSameLayoutTheAsynchronousOneGives()
        {
            const string html = "<p style='margin:0'>Hello <b>world</b></p><div style='height:40pt'></div>";
            var (syncContext, _) = NewContext(completed: true);
            using var syncDocument = LoadAndLayout(syncContext, html);

            var (asyncContext, _) = NewContext(completed: true);
            using var asyncDocument = await HtmlDocument.LoadAsync(html, asyncContext, new HtmlDocumentOptions { MediaType = MediaType.Screen });
            var area = asyncDocument.ContentArea;
            using var measuring = PdfCanvas.CreateMeasuring(asyncContext, area.Width, area.Height);
            await asyncDocument.LayoutAsync(measuring);

            Assert.Equal(asyncDocument.Pages.Count, syncDocument.Pages.Count);
            Assert.Equal(asyncDocument.ContentExtent, syncDocument.ContentExtent);
        }

        [Fact]
        public void TheSynchronousEntryPoints_RequireTheOption()
        {
            var (context, _) = NewContext(completed: true);

            Assert.Throws<InvalidOperationException>(() => HtmlDocument.Load("<p>x</p>", context, new HtmlDocumentOptions()));
            Assert.Throws<ArgumentNullException>(() => HtmlDocument.Load("<p>x</p>", context, null!));
        }

        [Fact]
        public async Task LayoutAndPaint_RequireADocumentLoadedWithTheOption()
        {
            var (context, _) = NewContext(completed: true);
            using var document = await HtmlDocument.LoadAsync("<p>x</p>", context, new HtmlDocumentOptions());
            using var measuring = PdfCanvas.CreateMeasuring(context, 100, 100);

            Assert.Throws<InvalidOperationException>(() => document.Layout(measuring));
            Assert.Throws<InvalidOperationException>(() => document.PaintPage(0, measuring));
        }

        [Theory]
        [InlineData("<link rel='stylesheet' href='style.css'><p>x</p>", "style.css")]
        [InlineData("<img src='photo.png'>", "photo.png")]
        [InlineData("<style>@import url('imported.css');</style><p>x</p>", "imported.css")]
        [InlineData("<style>@font-face { font-family: Web; src: url('https://example.test/web.woff2') }</style><p style='font-family:Web'>x</p>", "web.woff2")]
        public void AResourceThatWouldSuspend_ThrowsNamingIt(string html, string resource)
        {
            var (context, _) = NewContext(completed: false);

            var thrown = Assert.ThrowsAny<InvalidOperationException>(() => LoadAndLayout(context, html));

            Assert.Contains(resource, thrown.Message);
            Assert.Contains("SynchronousOnly", thrown.Message);
        }

        [Fact]
        public void AResourceThatIsAlreadyAvailable_IsAcceptedAndUsed()
        {
            var (context, loader) = NewContext(completed: true);
            using var document = LoadAndLayout(context, "<link rel='stylesheet' href='style.css'><p style='margin:0'>x</p>");

            Assert.Contains(Origin + "style.css", loader.Requested);
        }

        [Fact]
        public async Task WithoutTheOption_APendingResourceIsStillAwaited()
        {
            var (context, _) = NewContext(completed: false);

            // Nothing throws; the load simply has not finished while the resource is outstanding.
            var load = HtmlDocument.LoadAsync("<link rel='stylesheet' href='style.css'>", context, new HtmlDocumentOptions { MediaType = MediaType.Screen }).AsTask();
            await Task.Delay(50, TestContext.Current.CancellationToken);
            Assert.False(load.IsCompleted, load.Status + " " + load.Exception);
        }
    }
}
